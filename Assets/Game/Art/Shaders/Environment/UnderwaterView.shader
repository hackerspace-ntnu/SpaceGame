// The view from under the water, drawn by UnderwaterRenderFeature while a camera is inside an
// UnderwaterVolume. One fullscreen pass over the finished opaque + transparent frame:
//
//   Haze      - distance fog toward a blue that is lighter looking up and darker looking down,
//               and darker the deeper the camera is. Everything far away dissolves into it.
//   Caustics  - two drifting layers of the caustic texture, min-combined, projected straight
//               down onto everything below the surface and fading with depth.
//   Surface   - where the view ray reaches the surface before any geometry, the surface seen
//               from beneath (the water material culls its back face, so this pass is the only
//               thing that draws it). The ray is refracted through the same rippled normal the
//               surface has from above. Steeper than the critical angle (~49 deg from straight
//               up) it passes out: Snell's window, the world above squeezed into a bright disc.
//               Shallower, it is totally internally reflected and the surface mirrors the
//               depths. Fresnel blends the two toward the window's rim.
//   Wobble    - a slow screen-space sway, so the whole picture feels like it is in water.

Shader "Hidden/UnderwaterView"
{
    Properties
    {
        [Header(Haze)]
        _FogColorShallow ("Haze Looking Up", Color) = (0.32, 0.66, 0.88, 1)
        _FogColorDeep    ("Haze Looking Down", Color) = (0.07, 0.27, 0.52, 1)
        _FogDensity      ("Haze Density (1/m)", Range(0, 0.5)) = 0.09
        _FogDepthRange   ("Depth to Darkest (m)", Float) = 10
        _SceneTint       ("Scene Tint", Color) = (0.55, 0.80, 0.95, 1)

        [Header(Caustics)]
        [NoScaleOffset] _CausticsMap ("Caustics", 2D) = "black" {}
        _CausticsColor    ("Caustics Colour", Color) = (0.75, 0.95, 1, 1)
        _CausticsScale    ("Caustics Scale (1/m)", Float) = 0.22
        _CausticsSpeed    ("Caustics Drift (per s)", Float) = 0.04
        _CausticsStrength ("Caustics Strength", Range(0, 3)) = 0.9
        _CausticsDepthFade("Caustics Fade Depth (m)", Float) = 6

        [Header(Surface From Below)]
        [NoScaleOffset] _WaterNoise ("Noise Volume (set from the water above)", 3D) = "" {}
        _RippleScale        ("Ripple Size (m)", Float) = 1.8
        _RippleStrength     ("Ripple Strength", Range(0, 1)) = 0.35
        _RippleDrift        ("Ripple Drift (m/s)", Float) = 0.25
        _RippleChange       ("Ripple Re-form Speed", Float) = 0.35
        _RippleFadeDistance ("Ripple Fade Distance (m)", Float) = 35
        _UndersideRipple    ("Ripple From Below (x above)", Range(0, 1)) = 0.4
        _WindowBrightness   ("Window Brightness", Range(0, 2)) = 1
        _WindowParallax     ("Window Parallax Distance (m)", Float) = 40
        _WindowScreenFade   ("Window Screen-Edge Fade (uv)", Range(0.001, 0.5)) = 0.1

        [Header(Wobble)]
        _WobbleStrength  ("Wobble Strength (UV)", Range(0, 0.02)) = 0.004
        _WobbleFrequency ("Wobble Frequency", Float) = 11
        _WobbleSpeed     ("Wobble Speed", Float) = 1.3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Underwater"

            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "StylizedWaterNoise.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord   : TEXCOORD0;
            };

            Varyings FullscreenVert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            TEXTURE2D(_CausticsMap);
            SAMPLER(sampler_CausticsMap);

            half4 _FogColorShallow;
            half4 _FogColorDeep;
            float _FogDensity;
            float _FogDepthRange;
            half4 _SceneTint;
            half4 _CausticsColor;
            float _CausticsScale;
            float _CausticsSpeed;
            half  _CausticsStrength;
            float _CausticsDepthFade;
            float _RippleScale;
            half  _RippleStrength;
            float _RippleDrift;
            float _RippleChange;
            float _RippleFadeDistance;
            half  _UndersideRipple;
            half  _WindowBrightness;
            float _WindowParallax;
            float _WindowScreenFade;
            float _WobbleStrength;
            float _WobbleFrequency;
            float _WobbleSpeed;
            float _WaterSurfaceHeight;   // set per camera by UnderwaterRenderFeature

            static const float SkyDistance = 10000.0;

            // Water to air. F0 is the head-on reflectance that index implies, ((n-1)/(n+1))^2.
            static const float WaterIor = 1.333;
            static const half  WaterF0 = 0.0204;

            half Caustics(float2 worldXZ)
            {
                float2 uv = worldXZ * _CausticsScale;
                float drift = _Time.y * _CausticsSpeed;
                half a = SAMPLE_TEXTURE2D(_CausticsMap, sampler_CausticsMap, uv + drift * float2(1.0, 0.6)).r;
                half b = SAMPLE_TEXTURE2D(_CausticsMap, sampler_CausticsMap, uv * 1.3 - drift * float2(0.7, 1.0)).r;
                return min(a, b);
            }

            // The haze along a direction: lighter looking up, darker looking down and deeper down.
            half3 Haze(float3 dir, float eyeDepth)
            {
                return lerp(_FogColorDeep.rgb, _FogColorShallow.rgb, saturate(0.5 + dir.y * 0.7))
                     * lerp(1.0, 0.6, eyeDepth);
            }

            bool IsSky(float rawDepth)
            {
            #if UNITY_REVERSED_Z
                return rawDepth <= 0.00001;
            #else
                return rawDepth >= 0.99999;
            #endif
            }

            // What a ray leaving the water at `hit` along `refracted` sees above it. The frame
            // already holds the world above the surface, unbent, so the bent ray is read back from
            // it a parallax distance out. Where that read is off screen, or lands on something
            // under the water, the unbent view through this pixel stands in: it is always the
            // world above too. (The sky probe cannot: it is Unity's default cubemap, not our sky.)
            half3 SeenAbove(float3 hit, float3 refracted, half3 unbent)
            {
                float3 target = hit + refracted * _WindowParallax;
                if (mul(UNITY_MATRIX_VP, float4(target, 1.0)).w <= 0.0)
                    return unbent;

                float2 uv = ComputeNormalizedDeviceCoordinates(target, UNITY_MATRIX_VP);
                float2 edge = min(uv, 1.0 - uv);
                half onScreen = saturate(min(edge.x, edge.y) / _WindowScreenFade);
                if (onScreen <= 0.0)
                    return unbent;

                float rawDepth = SampleSceneDepth(uv);
                if (!IsSky(rawDepth) && ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP).y < _WaterSurfaceHeight)
                    return unbent;

                half3 bent = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                return lerp(unbent, bent, onScreen);
            }

            // The surface seen from below at `hit`, reached after `travelled` metres along `dir`;
            // `unbent` is what the frame holds straight through it.
            half3 SurfaceFromBelow(float3 hit, float3 dir, float travelled, float eyeDepth, half3 unbent)
            {
                // Far ripples are finer than a pixel and would shimmer, as from above. Refraction
                // bends far more than reflection does, so the same slope from below would shatter
                // the window's rim into islands; it is scaled down.
                float fade = lerp(0.1, 1.0, saturate(1.0 - travelled / _RippleFadeDistance));
                float2 slope = WaterRippleSlope(hit.xz, _RippleScale, _RippleDrift, _RippleChange, _Time.y)
                             * _RippleStrength * _UndersideRipple * fade;

                // The rippled normal, facing down into the water the ray comes from.
                float3 normal = normalize(float3(slope.x, -1.0, slope.y));
                half3 depths = Haze(reflect(dir, normal), eyeDepth);

                float3 refracted = refract(dir, normal, WaterIor);
                if (dot(refracted, refracted) < 1e-6)
                    return depths;

                half cosAir = saturate(dot(refracted, -normal));
                half fresnel = WaterF0 + (1.0 - WaterF0) * pow(1.0 - cosAir, 5.0);
                return lerp(SeenAbove(hit, refracted, unbent) * _WindowBrightness, depths, fresnel);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y * _WobbleSpeed;
                float2 wobble = float2(sin(IN.texcoord.y * _WobbleFrequency + t),
                                       cos(IN.texcoord.x * _WobbleFrequency * 0.8 + t * 1.3)) * _WobbleStrength;
                float2 uv = IN.texcoord + wobble;

                half3 scene = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv).rgb;
                float rawDepth = SampleSceneDepth(uv);
                bool sky = IsSky(rawDepth);

                float3 eye = _WorldSpaceCameraPos;
                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 toPixel = worldPos - eye;
                float distance = sky ? SkyDistance : length(toPixel);
                float3 dir = toPixel / max(length(toPixel), 1e-4);

                float eyeDepth = saturate((_WaterSurfaceHeight - eye.y) / max(_FogDepthRange, 0.01));

                // Does the ray reach the surface before it reaches any geometry?
                float toSurface = dir.y > 1e-4 ? (_WaterSurfaceHeight - eye.y) / dir.y : SkyDistance * 2.0;
                half3 colour;
                if (toSurface < distance)
                {
                    colour = SurfaceFromBelow(eye + dir * toSurface, dir, toSurface, eyeDepth, scene);
                    distance = toSurface;
                }
                else
                {
                    colour = scene * _SceneTint.rgb;
                    float below = _WaterSurfaceHeight - worldPos.y;
                    if (!sky && below > 0.0)
                        colour += _CausticsColor.rgb * Caustics(worldPos.xz)
                                * _CausticsStrength * exp(-below / max(_CausticsDepthFade, 0.01));
                }

                float fog = 1.0 - exp(-distance * _FogDensity);
                return half4(lerp(colour, Haze(dir, eyeDepth), fog), 1.0);
            }
            ENDHLSL
        }
    }
}
