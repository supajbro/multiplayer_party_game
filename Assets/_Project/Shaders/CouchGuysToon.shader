Shader "Couch Guys/Toon Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Colour", Color) = (1, 1, 1, 1)
        _Saturation("Colour Saturation", Range(0, 2)) = 1.1

        [Header(Toon Lighting)]
        _ShadowColor("Shadow Tint", Color) = (0.42, 0.48, 0.62, 1)
        _ShadowStrength("Shadow Strength", Range(0, 1)) = 0.62
        _ShadowThreshold("Light Threshold", Range(-0.25, 1)) = 0.38
        _BandSoftness("Band Softness", Range(0.001, 0.5)) = 0.06
        _AdditionalLightStrength("Additional Lights", Range(0, 2)) = 0.65

        [Header(Rim Light)]
        [Toggle] _RimEnabled("Enable Rim", Float) = 1
        [HDR] _RimColor("Rim Colour", Color) = (0.45, 0.72, 1, 1)
        _RimPower("Rim Power", Range(0.5, 8)) = 3
        _RimIntensity("Rim Intensity", Range(0, 2)) = 0.18

        [Header(Specular)]
        [Toggle] _SpecularEnabled("Enable Specular", Float) = 0
        [HDR] _SpecColor("Specular Colour", Color) = (1, 1, 1, 1)
        _SpecularSize("Specular Size", Range(0.01, 1)) = 0.2
        _SpecularSoftness("Specular Softness", Range(0.001, 0.25)) = 0.04
        _SpecularIntensity("Specular Intensity", Range(0, 2)) = 0.35

        [Header(Emission)]
        _EmissionMap("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor("Emission Colour", Color) = (0, 0, 0, 0)

        [Header(Surface Options)]
        [Toggle] _AlphaClip("Alpha Clipping", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ToonVertex
            #pragma fragment ToonFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _RimColor;
                half4 _SpecColor;
                half4 _EmissionColor;
                half _Saturation;
                half _ShadowStrength;
                half _ShadowThreshold;
                half _BandSoftness;
                half _AdditionalLightStrength;
                half _RimEnabled;
                half _RimPower;
                half _RimIntensity;
                half _SpecularEnabled;
                half _SpecularSize;
                half _SpecularSoftness;
                half _SpecularIntensity;
                half _AlphaClip;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    float4 shadowCoord : TEXCOORD3;
                #endif
                half fogFactor : TEXCOORD4;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 5);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ToonVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    output.shadowCoord = GetShadowCoord(positions);
                #endif
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                return output;
            }

            half ToonBand(half value)
            {
                half softness = max(_BandSoftness, 0.001h);
                return smoothstep(_ShadowThreshold - softness, _ShadowThreshold + softness, value);
            }

            half3 SaturateColour(half3 colour)
            {
                half luminance = dot(colour, half3(0.2126h, 0.7152h, 0.0722h));
                return max(0.0h, lerp(luminance.xxx, colour, _Saturation));
            }

            half4 ToonFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(lerp(1.0h, baseSample.a - _Cutoff, step(0.5h, _AlphaClip)));

                half3 normalWS = normalize(input.normalWS);
                float4 shadowCoord;
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    shadowCoord = input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    // Cascades must be selected per pixel. Selecting them in the vertex
                    // shader interpolates coordinates from different atlas tiles across
                    // large procedural terrain triangles, creating camera-moving bands.
                    shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    shadowCoord = float4(0.0, 0.0, 0.0, 0.0);
                #endif
                Light mainLight = GetMainLight(shadowCoord);
                half mainNdotL = saturate(dot(normalWS, mainLight.direction));
                half mainBand = ToonBand(mainNdotL * mainLight.shadowAttenuation);
                half3 shadowTone = lerp(1.0h.xxx, _ShadowColor.rgb, _ShadowStrength);
                half3 directLighting = lerp(shadowTone, 1.0h.xxx, mainBand) * mainLight.color * mainLight.distanceAttenuation;

                half3 bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, normalWS);
                half3 lighting = max(directLighting, bakedGI * shadowTone);

                #if defined(_ADDITIONAL_LIGHTS)
                    uint additionalLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(additionalLightCount)
                        Light light = GetAdditionalLight(lightIndex, input.positionWS);
                        half attenuation = light.distanceAttenuation * light.shadowAttenuation;
                        half band = ToonBand(saturate(dot(normalWS, light.direction)) * attenuation);
                        lighting += light.color * band * attenuation * _AdditionalLightStrength;
                    LIGHT_LOOP_END
                #endif

                half3 colour = baseSample.rgb * lighting;
                [branch] if (_RimEnabled >= 0.5h)
                {
                    half3 rimViewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                    half fresnel = pow(saturate(1.0h - dot(normalWS, rimViewDirectionWS)), _RimPower);
                    colour += _RimColor.rgb * fresnel * _RimIntensity;
                }

                [branch] if (_SpecularEnabled >= 0.5h)
                {
                    half3 specularViewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                    half3 halfDirection = SafeNormalize(mainLight.direction + specularViewDirectionWS);
                    half specularTerm = saturate(dot(normalWS, halfDirection));
                    half specularThreshold = 1.0h - _SpecularSize;
                    half toonSpecular = smoothstep(
                        specularThreshold - _SpecularSoftness,
                        specularThreshold + _SpecularSoftness,
                        specularTerm);
                    colour += _SpecColor.rgb * toonSpecular * mainBand * _SpecularIntensity;
                }

                half3 emission = 0.0h;
                [branch] if (any(_EmissionColor.rgb != 0.0h))
                    emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
                colour = SaturateColour(colour) + emission;
                colour = MixFog(colour, input.fogFactor);
                return half4(colour, baseSample.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _RimColor;
                half4 _SpecColor;
                half4 _EmissionColor;
                half _Saturation;
                half _ShadowStrength;
                half _ShadowThreshold;
                half _BandSoftness;
                half _AdditionalLightStrength;
                half _RimEnabled;
                half _RimPower;
                half _RimIntensity;
                half _SpecularEnabled;
                half _SpecularSize;
                half _SpecularSoftness;
                half _SpecularIntensity;
                half _AlphaClip;
                half _Cutoff;
            CBUFFER_END
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings ShadowVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                clip(lerp(1.0h, alpha - _Cutoff, step(0.5h, _AlphaClip)));
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull [_Cull]
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _RimColor;
                half4 _SpecColor;
                half4 _EmissionColor;
                half _Saturation;
                half _ShadowStrength;
                half _ShadowThreshold;
                half _BandSoftness;
                half _AdditionalLightStrength;
                half _RimEnabled;
                half _RimPower;
                half _RimIntensity;
                half _SpecularEnabled;
                half _SpecularSize;
                half _SpecularSoftness;
                half _SpecularIntensity;
                half _AlphaClip;
                half _Cutoff;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings DepthVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }
            half4 DepthFragment(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                clip(lerp(1.0h, alpha - _Cutoff, step(0.5h, _AlphaClip)));
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
