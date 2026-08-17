// Wind-blown garment cloth: capes, scarves, tabards.
//
// The garment counterpart to SailCloth. A sail is a flat plane pinned along three lashed edges
// and its UVs are authored as real distances, so it can pin by UV. A cape is a skinned mesh
// hanging off a character, so it pins by height instead: the collar is stitched to the
// shoulders, the hem is free, and _AnchorAxis / _FreeLength describe that span in object space.
//
//   Billow  - a long wave travelling along the wind, carrying the silhouette.
//   Flutter - two octaves of scrolling noise weighted to the hem, the edge that shakes first.
//   Gust    - a slow swell and drop over the whole thing, so the wind breathes.
//   Pinning - ClothFreedom() holds the collar still. Without it the wind translates every
//             vertex equally and the cape slides off the character as a rigid sheet.
//
// The displacement runs after skinning, so it layers on top of the Animator rather than
// fighting it. _WindDirection and _WindStrength are written by NomadCapeWind through a
// MaterialPropertyBlock, so every garment on the character shares one material.

Shader "SpaceGame/ClothWind"
{
    Properties
    {
        [MainTexture] _BaseMap   ("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor ("Base Colour", Color) = (0.42, 0.34, 0.26, 1)

        _Smoothness   ("Smoothness", Range(0,1)) = 0.12
        _Metallic     ("Metallic", Range(0,1)) = 0.0
        _Backlight    ("Sun Through Cloth", Range(0,2)) = 0.55

        [Header(Weave)]
        _WeaveScale   ("Weave Scale", Float) = 22.0
        _WeaveDepth   ("Weave Depth", Range(0,1)) = 0.10

        [Header(Wind)]
        _WindDirection("Wind Direction (world XYZ)", Vector) = (1, 0, 0.35, 0)
        _WindStrength ("Wind Strength (m)", Range(0,2)) = 0.22
        _Turbulence   ("Turbulence", Range(0,1)) = 0.30

        [Header(Billow)]
        _WaveSpeed    ("Billow Speed", Range(0,12)) = 2.2
        _WaveLength   ("Billow Length (m)", Range(0.05,8)) = 1.6

        [Header(Flutter)]
        _FlutterAmp   ("Flutter Amplitude (m)", Float) = 0.12
        _FlutterFreq  ("Flutter Frequency", Float) = 2.4
        _FlutterSpeed ("Flutter Speed", Float) = 5.0

        [Header(Gust)]
        _GustSpeed    ("Gust Speed", Range(0,4)) = 0.55
        _GustAmount   ("Gust Depth", Range(0,1)) = 0.45

        [Header(Anchoring)]
        // Which object-space axis runs from the stitched collar to the free hem.
        // 0 = X, 1 = Y, 2 = Z.
        _AnchorAxis   ("Anchor Axis (0=X 1=Y 2=Z)", Range(0,2)) = 1
        _AnchorOrigin ("Anchor Plane (object space)", Float) = 0
        _FreeLength   ("Free Length (signed, object space)", Float) = -1.0
        _Stiffness    ("Stiffness", Range(0.5,4)) = 1.6
        _MaxStretch   ("Max Displacement (m)", Range(0,3)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // Cloth is a thin sheet — show both sides or the cape vanishes edge-on.
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
            #include "ClothWind.hlsl"

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

                // The rest position drives the pinning, so a cape already blown out to the side
                // is still measured from the collar it started at.
                ShapeCloth(IN.positionOS.xyz, positionOS, normalOS);

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
                albedo.rgb *= ClothWeave(IN.uv);

                // VFACE is negative on back faces; cloth lit from behind must not go black.
                float3 normalWS = normalize(IN.normalWS) * (facing > 0 ? 1.0 : -1.0);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float ndotl = saturate(dot(normalWS, mainLight.direction));
                float3 lighting = mainLight.color * mainLight.shadowAttenuation * ndotl;

                // Sun through the cloth: thin fabric glows where the sun is behind it, which is
                // most of what sells a cape as cloth rather than painted board.
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
        // Shadows have to run the same displacement or the cape casts a rigid shadow.
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
            #include "ClothWind.hlsl"

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
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
                ShapeCloth(IN.positionOS.xyz, positionOS, normalOS);

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
        // Depth prepass, likewise displaced so depth matches what the forward pass drew.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ClothWind.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
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
                ShapeCloth(IN.positionOS.xyz, positionOS, normalOS);

                OUT.positionCS = TransformObjectToHClip(positionOS);
                return OUT;
            }

            half4 depthFrag(DepthVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
