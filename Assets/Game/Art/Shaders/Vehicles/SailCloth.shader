// Sailcloth: inflates a flat sail mesh into a belly and flutters it.
//
// The source sails are flat planes. Everything that makes them read as cloth happens here:
//
//   Billow  - displacement along the sail's normal, pushed to leeward, shaped so the deepest
//             point sits around 40% of the chord aft of the luff. That is where the draft of a
//             real sail is; putting it at the middle looks like a bedsheet.
//   Flutter - two octaves of scrolling noise, weighted toward the free leech, because a sail
//             shakes at its unattached edge first.
//   Pinning - the luff is lashed to the post and the foot and head to their spars, so those
//             edges must not move at all or the cloth tears away from its own rig.
//
// UVs come from dune_foil_rig.py: U = 0 at the luff, 1 at the leech; V = 0 at the foot, 1 at
// the head. Both are real distances measured against the spar, not whatever the source plane
// happened to carry.
//
// _Billow, _Luff, _Hoist and _WindDirection are written per-sail by SailSurface through a
// MaterialPropertyBlock, so every sail on the craft shares one material.

Shader "SpaceGame/SailCloth"
{
    Properties
    {
        [MainTexture] _BaseMap        ("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor      ("Base Colour", Color) = (0.90, 0.71, 0.46, 1)

        _Smoothness   ("Smoothness", Range(0,1)) = 0.08

        [Header(Weave)]
        _WeaveScale   ("Weave Scale", Float) = 18.0
        _WeaveDepth   ("Weave Depth", Range(0,1)) = 0.12

        [Header(Billow)]
        _Billow       ("Billow Amount", Range(0,1)) = 0.6
        _BillowDepth  ("Billow Depth (m)", Float) = 1.35
        _DraftPosition("Draft Position", Range(0.15,0.75)) = 0.4

        [Header(Flutter)]
        _Luff         ("Luff Amount", Range(0,1)) = 0.0
        _FlutterAmp   ("Flutter Amplitude (m)", Float) = 0.45
        _FlutterFreq  ("Flutter Frequency", Float) = 2.6
        _FlutterSpeed ("Flutter Speed", Float) = 5.0

        [Header(State)]
        _Hoist        ("Hoist 0-1", Range(0,1)) = 1.0
        _WindDirection("Wind Direction", Vector) = (0,0,1,0)

        [Header(Translucency)]
        _Backlight    ("Sun Through Cloth", Range(0,2)) = 0.7
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // A sail is a surface with two sides and the wind decides which one you see.
        Cull Off

        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SailCloth.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionOS = IN.positionOS.xyz;
                float3 normalOS   = IN.normalOS;

                ShapeSail(IN.uv, positionOS, normalOS);

                VertexPositionInputs pos = GetVertexPositionInputs(positionOS);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(normalOS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN, half facing : VFACE) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                albedo.rgb *= WeavePattern(IN.uv);

                // VFACE is negative on back faces; a sail lit from behind must not go black.
                float3 normalWS = normalize(IN.normalWS) * (facing > 0 ? 1.0 : -1.0);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float ndotl = saturate(dot(normalWS, mainLight.direction));
                float3 lighting = mainLight.color * mainLight.shadowAttenuation * ndotl;

                // Sun through the cloth: thin canvas glows where the sun is behind it, which is
                // most of what sells a sail as fabric rather than painted board.
                float backside = saturate(dot(-normalWS, mainLight.direction));
                lighting += mainLight.color * pow(backside, 2.0) * _Backlight
                            * mainLight.shadowAttenuation;

                lighting += SampleSH(normalWS);

                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint i = 0u; i < count; ++i)
                {
                    Light l = GetAdditionalLight(i, IN.positionWS);
                    lighting += l.color * l.distanceAttenuation * l.shadowAttenuation
                                * saturate(dot(normalWS, l.direction));
                }
                #endif

                half3 colour = albedo.rgb * lighting;
                colour = MixFog(colour, IN.fogFactor);
                return half4(colour, albedo.a);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Shadows have to run the same displacement or the sail casts a flat shadow.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "SailCloth.hlsl"

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings shadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 positionOS = IN.positionOS.xyz;
                float3 normalOS   = IN.normalOS;
                ShapeSail(IN.uv, positionOS, normalOS);

                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS   = TransformObjectToWorldNormal(normalOS);

                OUT.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                OUT.positionCS.z = min(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                OUT.positionCS.z = max(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return OUT;
            }

            half4 shadowFrag(ShadowVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SailCloth.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings depthVert(DepthAttributes IN)
            {
                DepthVaryings OUT = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 positionOS = IN.positionOS.xyz;
                float3 normalOS   = IN.normalOS;
                ShapeSail(IN.uv, positionOS, normalOS);

                OUT.positionCS = TransformObjectToHClip(positionOS);
                return OUT;
            }

            half4 depthFrag(DepthVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
