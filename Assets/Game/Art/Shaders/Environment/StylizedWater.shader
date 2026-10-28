// Cozy stylized water for ponds and lakes. The layers live in StylizedWater.hlsl, one function
// each; this file only stacks them.
//
// The water composites the scene behind it itself — it reads the camera's opaque texture and
// depth texture and writes the finished colour — so it is drawn in the transparent queue with
// blending off. Both textures must be enabled on the URP asset or the bed reads as black.

Shader "SpaceGame/StylizedWater"
{
    Properties
    {
        [NoScaleOffset] _WaterNoise    ("Noise Volume (SpaceGame > Environment > Bake Water Noise)", 3D) = "" {}
        [NoScaleOffset] _WaterCaustics ("Caustic Web (baked with the noise)", 2D) = "black" {}

        [Header(Colour by depth)]
        _ShoreColor   ("Shore", Color) = (0.62, 0.92, 0.93, 1)
        _ShallowColor ("Shallow", Color) = (0.30, 0.74, 0.92, 1)
        _DeepColor    ("Deep", Color) = (0.10, 0.43, 0.80, 1)
        _ShallowDepth ("Shallow Depth (m)", Float) = 0.6
        _DeepDepth    ("Deep Depth (m)", Float) = 2.4

        [Header(See through)]
        _Clarity    ("Clarity (m of water per fade)", Float) = 1.5
        _MaxOpacity ("Max Opacity", Range(0, 1)) = 0.88
        _BedTint    ("Bed Tint Under Water", Color) = (0.62, 0.86, 0.92, 1)

        [Header(Colour drift)]
        _DriftStrength ("Drift Strength", Range(0, 0.5)) = 0.12
        _DriftScale    ("Drift Size (m)", Float) = 14
        _DriftSpeed    ("Drift Change Speed", Float) = 0.05

        [Header(Swells)]
        _SwellA     ("Swell A (dir xy, steepness, length m)", Vector) = (1, 0.35, 0.05, 6)
        _SwellB     ("Swell B (dir xy, steepness, length m)", Vector) = (-0.4, 1, 0.04, 9)
        _SwellC     ("Swell C (dir xy, steepness, length m)", Vector) = (0.6, -0.8, 0.03, 3.5)
        _SwellSpeed ("Swell Speed", Range(0, 2)) = 0.35

        [Header(Ripples)]
        _RippleScale        ("Ripple Size (m)", Float) = 1.8
        _RippleStrength     ("Ripple Strength", Range(0, 1)) = 0.35
        _RippleDrift        ("Ripple Drift (m/s)", Float) = 0.25
        _RippleChange       ("Ripple Re-form Speed", Float) = 0.35
        _RippleFadeDistance ("Ripple Fade Distance (m)", Float) = 35

        [Header(Refraction)]
        _Refraction ("Refraction (screen fraction)", Range(0, 0.2)) = 0.04

        [Header(Shade)]
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.35
        _ShadowTint     ("Shadow Tint", Color) = (0.75, 0.85, 1, 1)

        [Header(Reflection)]
        _ReflectionStrength   ("Reflection Strength", Range(0, 1)) = 0.45
        _ReflectionMinimum    ("Reflection Looking Straight Down", Range(0, 1)) = 0.05
        _FresnelPower         ("Fresnel Power", Range(0.5, 8)) = 5
        _ReflectionDistortion ("Reflection Distortion", Range(0, 0.3)) = 0.04
        _ProbeRoughness       ("Probe Fallback Roughness", Range(0, 1)) = 0.15
        [HideInInspector] _PlannerReflectionBlend ("Planar Reflection Blend", Float) = 1

        [Header(Caustics)]
        [HDR] _CausticColor     ("Caustic Light", Color) = (0.9, 1, 1, 1)
        _CausticScale        ("Caustic Cell Size (m)", Float) = 1.4
        _CausticSpeed        ("Caustic Speed (cells/s)", Float) = 0.12
        _CausticWarp         ("Caustic Warp (cells)", Range(0, 1)) = 0.35
        _CausticThickness    ("Caustic Line Width", Range(0.01, 0.4)) = 0.07
        _CausticDepth        ("Caustics Gone By Depth (m)", Float) = 2.2
        _CausticFadeDistance ("Caustic Fade Distance (m)", Float) = 45

        [Header(Glints)]
        [HDR] _GlintColor ("Glint", Color) = (1.6, 1.55, 1.4, 1)
        _GlintPower       ("Glint Tightness", Range(8, 4096)) = 2500
        _GlintThreshold   ("Glint Threshold", Range(0, 1)) = 0.55
        _GlintSoftness    ("Glint Edge Softness", Range(0.001, 1)) = 0.3

        [Header(Foam)]
        _FoamColor        ("Foam", Color) = (0.97, 0.99, 1, 1)
        _FoamNoiseScale   ("Foam Breakup Size (m)", Float) = 0.9
        _FoamChange       ("Foam Re-form Speed", Float) = 0.4
        _RimWidth         ("Shore Rim Width (m)", Float) = 0.18
        _ShoreLineSpacing ("Shore Line Spacing (m of depth)", Float) = 0.22
        _ShoreLineSpeed   ("Shore Line Speed", Float) = 0.3
        _ShoreLineWidth   ("Shore Line Width", Range(0, 1)) = 0.22
        _ShoreLineReach   ("Shore Lines Reach (m of depth)", Float) = 0.5
        _StreakScale      ("Streak Size (m)", Float) = 3
        _StreakDrift      ("Streak Drift", Float) = 0.12
        _StreakWidth      ("Streak Width (cells)", Range(0, 0.2)) = 0.025
        _StreakCoverage   ("Streak Coverage", Range(0, 1)) = 0.22
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardWater"
            Tags { "LightMode" = "UniversalForward" }

            Blend Off
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "StylizedWater.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                half2  slow       : TEXCOORD3;   // colour drift, streak patch
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.slow = SlowPatterns(OUT.positionWS, _Time.y);
                ApplySwells(OUT.positionWS, OUT.normalWS, _Time.y);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float time = _Time.y;
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float3 normalWS = RippledNormal(normalize(IN.normalWS), IN.positionWS, time);

                WaterScene scene = ReadScene(screenUV, IN.positionWS, normalWS);
                Light sun = GetMainLight();
                sun.shadowAttenuation = SunShadowAt(IN.positionWS);

                // The bed, lit by caustics, under the shaded body of the water.
                half3 bed = SeenBed(scene) + Caustics(scene, normalWS, time, sun);
                half3 body = ShadeBody(BodyColour(scene, IN.slow.x), sun);
                half3 colour = lerp(bed, body, BodyOpacity(scene));

                // The surface: reflection, foam over it, glints on the bare water.
                colour = lerp(colour, ReflectedColour(screenUV, normalWS, viewDirWS), ReflectionAmount(normalWS, viewDirWS));
                half foam = max(ShoreFoam(scene, IN.positionWS, time), SurfaceStreaks(IN.positionWS, time, IN.slow.y));
                colour = lerp(colour, ShadeBody(_FoamColor.rgb, sun), foam * _FoamColor.a);
                colour += SunGlints(normalWS, viewDirWS, sun) * (1.0 - foam);

                return half4(MixFog(colour, IN.fogFactor), 1);
            }
            ENDHLSL
        }
    }
}
