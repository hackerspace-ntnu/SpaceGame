// Cozy, painterly meadow grass drawn by GrassField.
//
// Not realistic on purpose. Every blade is lit as if it were the ground it grows from — the
// normal is straight up — so a field shades as one soft surface instead of thousands of
// competing slivers. That single choice is most of the look; the gradient, the patch noise and
// the wind-brightening in StylizedGrass.hlsl do the rest.
//
// The tufts are opaque geometry with no alpha test, so the depth prepass and early-z stay
// cheap however dense the field gets. They receive the sun's shadow but never cast one: a
// meadow casting shadows is the single most expensive thing grass can do, and it reads as
// noise at this blade size anyway.
//
// Built for overdraw: dense, overlapping blades shade the same pixel many times, so all the
// lighting is done per vertex and a pixel only picks the flower colour, takes ONE hard shadow
// tap (soft-shadow filtering is deliberately not compiled in) and applies fog.

Shader "SpaceGame/StylizedGrass"
{
    Properties
    {
        [Header(Colour)]
        _RootColor     ("Root (match the ground)", Color) = (0.29, 0.39, 0.15, 1)
        _TipColor      ("Tip", Color) = (0.40, 0.53, 0.25, 1)
        _GradientPower ("Gradient Power", Range(0.2, 4)) = 0.9
        _TintVariation ("Per-Tuft Tint Variation", Range(0, 0.6)) = 0.16

        [Header(Patches)]
        _PatchColor    ("Patch Tip Colour", Color) = (0.46, 0.56, 0.24, 1)
        _PatchScale    ("Patch Scale (m)", Float) = 7
        _PatchStrength ("Patch Strength", Range(0, 1)) = 0.45

        [Header(Variation)]
        _VariationColorA   ("Variation Tip A (cool)", Color) = (0.33, 0.50, 0.21, 1)
        _VariationColorB   ("Variation Tip B (warm)", Color) = (0.50, 0.48, 0.20, 1)
        _VariationScale    ("Variation Clump Size (m)", Float) = 4
        _VariationStrength ("Variation Strength", Range(0, 1)) = 0.6
        _BladeBrightness   ("Per-Blade Brightness Jitter", Range(0, 0.6)) = 0.22
        _BladeTintColor    ("Per-Blade Warm Tint", Color) = (0.55, 0.38, 0.18, 1)
        _BladeTintAmount   ("Per-Blade Warm Tint Amount", Range(0, 1)) = 0.45

        [Header(Flowers)]
        _AccentColorA  ("Flower Colour A", Color) = (0.97, 0.62, 0.78, 1)
        _AccentColorB  ("Flower Colour B", Color) = (0.66, 0.58, 0.98, 1)
        _AccentSize    ("Flower Size (fraction of blade)", Range(0, 0.5)) = 0.16

        [Header(Lighting)]
        _LightWrap       ("Light Wrap", Range(0, 1)) = 0.6
        _BladeNormalBlend("Blade Facing in Lighting", Range(0, 1)) = 0.35
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1.0
        _ShadowStrength  ("Received Shadow Strength", Range(0, 1)) = 0.75
        _Translucency    ("Back-lit Tip Glow", Range(0, 2)) = 0.45

        [Header(Wind)]
        _WindStrength    ("Lean (m per m of blade)", Range(0, 2)) = 0.4
        _WaveLength      ("Wave Length (m)", Float) = 9
        _WaveSpeed       ("Wave Speed (m/s)", Float) = 1.8
        _WindFrequency   ("Gust Size (1/m)", Float) = 0.12
        _WindSpeed       ("Gust Speed (m/s)", Float) = 4
        _FlutterStrength ("Flutter", Range(0, 0.5)) = 0.04
        _FlutterFrequency ("Flutter Speed (rad/s)", Range(0, 12)) = 3.5
        _GustBrighten    ("Gust Brighten", Range(0, 1)) = 0.3

        [Header(Sheen)]
        _SheenStrength   ("Sheen (bent blades show their backs)", Range(0, 1)) = 0.45
        _SheenLift       ("Back Brightness", Range(0.5, 2)) = 1.3
        _SheenDesaturate ("Back Paleness", Range(0, 1)) = 0.45

        [Header(Plants)]
        _ContactShade    ("Contact Shade (beside trees and bushes)", Range(0, 1)) = 0.4

        [Header(Interaction)]
        _BendStrength  ("Bender Push", Range(0, 3)) = 1.4
        _TrampleStrength ("Trampled Trail (how flat it lies)", Range(0, 1.5)) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // Blades are single sheets.
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "StylizedGrass.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float2 uv         : TEXCOORD0;   // x: per-blade seed
            float3 morphOS    : TEXCOORD1;   // where this vertex sits on the next detail level's tuft
            float3 morphNormalOS : TEXCOORD2;   // and that tuft's normal there
            uint   instanceID : SV_InstanceID;
        };
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
                half3  albedo     : TEXCOORD1;
                half3  ambient    : TEXCOORD2;   // sky light
                half3  direct     : TEXCOORD3;   // sun light, before its shadow
                float3 flowerFog  : TEXCOORD4;   // x flower hue, y height01, z fog factor
            };

            Varyings vert(Attributes IN)
            {
                GrassVertex v = PlaceGrassVertex(IN.positionOS.xyz, IN.normalOS, IN.morphOS, IN.morphNormalOS, IN.uv.x, IN.instanceID);
                Varyings OUT;
                OUT.positionWS = v.positionWS;
                OUT.positionCS = TransformWorldToHClip(v.positionWS);
                float3 toCamera = normalize(_WorldSpaceCameraPos - v.positionWS);
                OUT.albedo = GrassAlbedo(v.height01, v.patch, v.tint, v.variation, v.bladeSeed, v.gust, v.canopy,
                                         v.contact, GrassSheen(v, toCamera));
                OUT.flowerFog = float3(v.accent, v.height01, ComputeFogFactor(OUT.positionCS.z));

                // Mostly the ground normal, wrapped, so the field shades as one soft surface; a
                // little of each blade's own facing separates the overlapping leaves. The side
                // facing the camera is the side that is lit, as a two-sided sheet should be.
                Light sun = GetMainLight();
                half3 up = half3(0, 1, 0);
                half3 blade = normalize(v.bladeNormalWS);
                blade *= dot(blade, toCamera) >= 0 ? 1.0 : -1.0;
                half3 normal = normalize(lerp(up, blade, _BladeNormalBlend));
                half wrapped = saturate((dot(normal, sun.direction) + _LightWrap) / (1.0 + _LightWrap));

                // Looking toward the sun, the thin tips let light through.
                half backlit = pow(saturate(dot(-toCamera, sun.direction)), 4.0) * v.height01 * _Translucency;

                OUT.ambient = SampleSH(up) * _AmbientStrength;
                OUT.direct = sun.color * (wrapped + backlit);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 albedo = GrassFlower(IN.albedo, IN.flowerFog.x, IN.flowerFog.y);
                Light sun = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                half shadow = lerp(1.0, sun.shadowAttenuation, _ShadowStrength);
                half3 colour = albedo * (IN.ambient + IN.direct * shadow);
                return half4(MixFog(colour, IN.flowerFog.z), 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // The water reads the depth texture for its shore foam, so the grass must be in it.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            float4 vert(Attributes IN) : SV_POSITION
            {
                return TransformWorldToHClip(PlaceGrassVertex(IN.positionOS.xyz, IN.normalOS, IN.morphOS, IN.morphNormalOS, IN.uv.x, IN.instanceID).positionWS);
            }

            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // SSAO reads this; the ground normal keeps occlusion off the individual blades.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            float4 vert(Attributes IN) : SV_POSITION
            {
                return TransformWorldToHClip(PlaceGrassVertex(IN.positionOS.xyz, IN.normalOS, IN.morphOS, IN.morphNormalOS, IN.uv.x, IN.instanceID).positionWS);
            }

            half4 frag() : SV_Target
            {
                float3 normalWS = float3(0, 1, 0);
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
