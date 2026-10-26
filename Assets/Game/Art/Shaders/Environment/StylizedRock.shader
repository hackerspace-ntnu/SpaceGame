// Rocks drawn by RockField: GPU instances placed by RockCull.compute, recoloured per biome.
//
// The look follows the grass and foliage: soft wrapped sun, sky ambient, ONE hard shadow tap
// (soft-shadow filtering is deliberately not compiled in), fog. The rock texture only lends its
// brightness pattern — cracks, edges, worn faces — and the material's ramp decides the colour,
// so a red desert and a yellow desert share one texture. See StylizedRock.hlsl.
//
// These materials cannot be drawn by an ordinary MeshRenderer: every pass reads _RockVisible.

Shader "SpaceGame/StylizedRock"
{
    Properties
    {
        [Header(Texture)]
        _BaseMap      ("Rock Texture (brightness drives the ramp)", 2D) = "white" {}
        _KeepOriginal ("Keep Texture Colours", Range(0, 1)) = 0

        [Header(Colour Ramp)]
        _DarkColor  ("Dark (cracks, shadowed faces)", Color) = (0.24, 0.13, 0.09, 1)
        _MidColor   ("Mid", Color) = (0.55, 0.30, 0.20, 1)
        _LightColor ("Light (worn edges)", Color) = (0.78, 0.55, 0.40, 1)
        _RampLow    ("Ramp Black Point", Range(0, 1)) = 0.2
        _RampHigh   ("Ramp White Point", Range(0, 1)) = 0.8

        [Header(Variation)]
        _VariationColor    ("Variation Colour", Color) = (0.50, 0.38, 0.28, 1)
        _VariationStrength ("Variation Strength", Range(0, 1)) = 0.35
        _TintVariation     ("Per-Rock Brightness", Range(0, 0.6)) = 0.2

        [Header(Dust)]
        _DustColor      ("Dust (match the ground)", Color) = (0.72, 0.52, 0.38, 1)
        _DustAmount     ("Dust Amount", Range(0, 1)) = 0.45
        _DustStart      ("Dust From Facing Up", Range(-1, 1)) = 0.55
        _DustSoftness   ("Dust Softness", Range(0.01, 1)) = 0.3
        _DustNoiseScale ("Dust Breakup Size (m)", Float) = 0.8

        [Header(Ground Contact)]
        _ContactDarken ("Contact Darkening", Range(0, 1)) = 0.3
        _ContactHeight ("Contact Height (mesh units)", Float) = 0.4

        [Header(Lighting)]
        _LightWrap       ("Light Wrap", Range(0, 1)) = 0.35
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1.0
        _ShadowStrength  ("Received Shadow Strength", Range(0, 1)) = 0.85
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "StylizedRock.hlsl"
        ENDHLSL

        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float3 tintContactFog : TEXCOORD3;   // x tint, y contact, z fog factor
            };

            Varyings vert(RockAttributes IN)
            {
                RockVertex v = PlaceRockVertex(IN);
                Varyings OUT;
                OUT.positionWS = v.positionWS;
                OUT.positionCS = TransformWorldToHClip(v.positionWS);
                OUT.normalWS = v.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.tintContactFog = float3(v.tint, v.contact, ComputeFogFactor(OUT.positionCS.z));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normal = normalize(IN.normalWS);
                half3 albedo = RockAlbedo(IN.uv, IN.positionWS, normal, IN.tintContactFog.x, IN.tintContactFog.y);
                Light sun = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                half wrapped = saturate((dot(normal, sun.direction) + _LightWrap) / (1.0 + _LightWrap));
                half shadow = lerp(1.0, sun.shadowAttenuation, _ShadowStrength);
                half3 colour = albedo * (SampleSH(normal) * _AmbientStrength + sun.color * wrapped * shadow);
                return half4(MixFog(colour, IN.tintContactFog.z), 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 vert(RockAttributes IN) : SV_POSITION
            {
                RockVertex v = PlaceRockVertex(IN);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirection = normalize(_LightPosition - v.positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(v.positionWS, v.normalWS, lightDirection));
                return ApplyShadowClamping(positionCS);
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // The water reads the depth texture for its shore foam, so the rocks must be in it.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            float4 vert(RockAttributes IN) : SV_POSITION
            {
                return TransformWorldToHClip(PlaceRockVertex(IN).positionWS);
            }

            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct NormalVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            NormalVaryings vert(RockAttributes IN)
            {
                RockVertex v = PlaceRockVertex(IN);
                NormalVaryings OUT;
                OUT.positionCS = TransformWorldToHClip(v.positionWS);
                OUT.normalWS = v.normalWS;
                return OUT;
            }

            half4 frag(NormalVaryings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormal = PackNormalOctQuadEncode(normalWS) * 0.5 + 0.5;
                return half4(PackFloat2To888(octNormal), 0);
            #else
                return half4(normalWS, 0);
            #endif
            }
            ENDHLSL
        }
    }
}
