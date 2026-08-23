// A lit surface that can be cut by a plane — for things standing half inside a
// portal.
//
// The traversal problem this solves: while a traveller straddles an aperture,
// PortalTraveller shows a second copy of it standing out of the linked portal.
// Both copies are whole, so the original pokes out of the back of the wall and
// the clone pokes out of the front of the other one. Clipping each copy against
// its own portal plane is what makes the two halves add up to one object.
//
// Opt-in by design. It cannot be done for arbitrary materials without replacing
// them, and replacing a character's authored material to pass through a portal
// is worse than the artefact it fixes — so the traveller code sets the plane on
// any renderer that exposes _SliceNormal and silently leaves the rest alone.
// Put this shader on things that go through portals a lot: crates, barrels,
// dropped items, anything simple enough that a plain lit shader is no loss.
Shader "SpaceGame/Portal/PortalSliceable"
{
    Properties
    {
        _BaseMap    ("Base map", 2D) = "white" {}
        _BaseColor  ("Base colour", Color) = (0.7, 0.7, 0.7, 1)
        _Smoothness ("Smoothness", Range(0.0, 1.0)) = 0.35
        _Metallic   ("Metallic", Range(0.0, 1.0)) = 0.0

        // Set per-renderer through a MaterialPropertyBlock by PortalTraveller.
        // A zero normal means "not slicing", which is the state every renderer
        // is in for all but the fraction of a second it spends in an aperture.
        _SliceNormal ("Slice normal (world)", Vector) = (0, 0, 0, 0)
        _SliceCentre ("Slice centre (world)", Vector) = (0, 0, 0, 0)
        // Lets the cut sit a hair behind the portal plane, so the seam is
        // hidden inside the aperture instead of shimmering on it.
        _SliceOffset ("Slice offset", Range(-0.5, 0.5)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Cull Off, because slicing exposes the inside of the mesh and a
            // back-face-culled cut looks like a hole rather than a cross
            // section. The normal is flipped per-fragment below to compensate.
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _SliceNormal;
                float4 _SliceCentre;
                float  _Smoothness;
                float  _Metallic;
                float  _SliceOffset;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.shadowCoord = GetShadowCoord(pos);
                return OUT;
            }

            half4 frag(Varyings IN, half facing : VFACE) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 n = _SliceNormal.xyz;
                float len = length(n);
                if (len > 1e-4)
                {
                    n /= len;
                    clip(dot(IN.positionWS - _SliceCentre.xyz, n) - _SliceOffset);
                }

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                SurfaceData surface = (SurfaceData)0;
                surface.albedo     = albedo.rgb;
                surface.alpha      = 1.0;
                surface.metallic   = _Metallic;
                surface.smoothness = _Smoothness;
                surface.occlusion  = 1.0;

                InputData input = (InputData)0;
                input.positionWS          = IN.positionWS;
                // VFACE is negative on back faces; flipping there is what keeps
                // the exposed interior lit instead of black.
                input.normalWS            = normalize(IN.normalWS) * (facing >= 0 ? 1.0 : -1.0);
                input.viewDirectionWS     = normalize(GetWorldSpaceViewDir(IN.positionWS));
                input.shadowCoord         = IN.shadowCoord;
                input.bakedGI             = SampleSH(input.normalWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionHCS);

                return UniversalFragmentPBR(input, surface);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _SliceNormal;
                float4 _SliceCentre;
                float  _Smoothness;
                float  _Metallic;
                float  _SliceOffset;
            CBUFFER_END

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionWS = positionWS;
                OUT.positionHCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));

            #if UNITY_REVERSED_Z
                OUT.positionHCS.z = min(OUT.positionHCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                OUT.positionHCS.z = max(OUT.positionHCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return OUT;
            }

            // The cut has to be repeated here or a sliced-away half keeps
            // casting its shadow, which is more obvious than the artefact the
            // slice was introduced to fix.
            half4 ShadowFrag(ShadowVaryings IN) : SV_Target
            {
                float3 n = _SliceNormal.xyz;
                float len = length(n);
                if (len > 1e-4)
                    clip(dot(IN.positionWS - _SliceCentre.xyz, n / len) - _SliceOffset);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
