// The rain column under a storm cloud — and, because the player stands in it, the INSIDE of the
// storm (Artifacts/StormFlask.md). Its shape, frame and guards come from StormCloudVolume.hlsl;
// read that first.
//
// ── WHY THE VEIL IS ITS OWN SHADER NOW ──────────────────────────────────────────────────────
//
// The surface version branched on the sign of positionOS.y and shaded the cloud above and the
// rain below out of one fragment program. That was the right shape for a painted lens, and it is
// the wrong shape for two VOLUMES: they do not share a bounding solid (an ellipsoid against an
// open cylinder), they do not share a march length (three metres against twenty-two), and they
// do not share a lighting model (a cloud is lit by the sun through itself, rain is lit by
// whatever is left after the cloud). One program doing both would branch on every sample.
//
// They still share one material property NAME — `_Form` and `_Flash`, which StormCloudLook paints
// into every renderer under the cloud without caring which shader is on the other end.
//
// ── THIS IS THE "FROM THE INSIDE" HALF OF THE EFFECT ────────────────────────────────────────
//
// There is no camera-parented quad, no fullscreen pass and no render feature. The march simply
// begins at the camera when the camera is inside the cylinder, so being under the storm IS being
// inside the volume: rain crosses in front of rain, the far wall is eleven metres away through
// twenty different curtains of it, and walking gives every streak the parallax of its own depth.
// The sandstorm needed a fullscreen fog pass for this because its interior is a kilometre across
// and no mesh could hold it; a twelve-metre column does not have that problem.
//
// Three things make the interior survivable rather than a grey screen, and all three are bounds
// rather than tuning — the sandstorm proved that tuning cannot fix any of them:
//
//   * _MaxOpacity caps the alpha. A Beer march always reaches 1 given enough distance.
//   * _ClearRadius holds the rain off the first metre or two, so the player can see their own
//     feet, their own hands and the ground they are standing on.
//   * The murk is a floor under the streaks, never a substitute for them. "Rainy, not foggy"
//     comes from the STREAKS — the murk's only job is to take the horizon away.
//
// ── WHAT MAKES THE RAIN VIOLENT ─────────────────────────────────────────────────────────────
//
//   * _RainWind slants every streak and DRIFTS each one sideways as it falls, so the rain is coming
//     at the player rather than dropping past them.
//   * _Gust runs travelling sheets of heavier rain across the column. Rain at constant density
//     reads as a texture; rain that surges reads as weather. It is also the cheapest possible
//     motion — one sine of a dot product.
//   * The columns carry their own density (`_ColumnBite`), so some parts of the curtain are
//     nearly solid and others are nearly open, and the difference moves past you.
//   * A splash haze at the foot, where the rain is landing. It is the only part of the veil that
//     is LIGHTER than what it covers, which is what makes the ground read as being hit.
Shader "SpaceGame/Artifacts/StormVeil"
{
    Properties
    {
        [Header(Mesh frame)]
        _MeshToUnit ("Mesh Units To Unit Radius", Float) = 100

        // The authored radius of Mesh_StormCloud_RainVolume, exactly. Unlike the cloud body's
        // ellipsoid there is nothing to shrink here: the mesh IS a cylinder, so the analytic
        // volume can sit on it, and the curtain is feathered inward by _RimFeather instead.
        [Header(Column)]
        _VeilRadius  ("Veil Radius",       Range(0.2, 1))   = 0.92
        _RimFeather  ("Rim Feather",       Range(0.01, 0.6)) = 0.50
        _ClearRadius ("Camera Clear (m)",  Range(0, 6))     = 2.5

        // Rain is DARKER than what it hangs in front of — both what a curtain of it really does
        // and the only version that stays readable, since the pale desert sky and the sand are
        // the two things it is most often seen against. The splash at the foot is the exception.
        [Header(Rain colour)]
        _RainDark   ("Rain Dark",   Color) = (0.322, 0.400, 0.475, 1)
        _RainMid    ("Rain Mid",    Color) = (0.451, 0.549, 0.643, 1)
        _RainLight  ("Rain Light",  Color) = (0.576, 0.620, 0.737, 1)
        _SplashTint ("Splash Tint", Color) = (0.710, 0.741, 0.835, 1)

        [Header(Rain)]
        _RainDensity ("Rain Density",         Range(0, 8))    = 8.0
        _Streaks     ("Streaks Per Veil",     Range(2, 40))   = 3
        _Columns     ("Columns",              Range(4, 200))  = 16
        _StreakWidth ("Streak Width",         Range(0.02, 1)) = 0.50
        _ColumnBite  ("Column Bite",          Range(0, 1))    = 0.92
        // At this world's gravity of 18 m/s^2 a drop released 15 m up arrives at about 23 m/s,
        // which over a 15 m veil is roughly 1.5 veil lengths a second. Deliberately not 9.81's
        // answer, which looks like drizzle here.
        _RainSpeed   ("Rain Speed (veils/s)", Range(0.1, 8))  = 1.6
        _RainTaper   ("Foot Taper",           Range(0, 1))    = 0.18

        [Header(Wind and gusts)]
        // xz is the direction the rain leans, its length is how far a drop drifts across the
        // whole fall. w is unused.
        _RainWind      ("Wind (xz drift)",     Vector)        = (0.22, 0, 0.1, 0)
        _Gust      ("Gust Depth",          Range(0, 1))   = 0.60
        _GustScale ("Gust Scale",          Range(0.2, 8)) = 1.7
        _GustSpeed ("Gust Speed",          Range(0, 6))   = 1.4

        [Header(Murk and splash)]
        _Murk       ("Murk Density",     Range(0, 2))     = 0.16
        _MurkTint   ("Murk Tint",        Color)          = (0.439, 0.475, 0.588, 1)
        _SplashBand ("Splash Band",      Range(0, 0.5))  = 0.16
        _Splash     ("Splash Density",   Range(0, 4))    = 1.1

        [Header(Light)]
        _Extinction ("Extinction (per m)", Range(0.01, 2)) = 0.50
        _SkyGain    ("Sky Gain",           Range(0, 4))    = 0.45
        _MaxOpacity ("Max Opacity",        Range(0.1, 1))  = 0.90

        [Header(Lightning)]
        _Flash      ("Flash",        Range(0, 1)) = 0
        _FlashColor ("Flash Colour", Color)       = (0.898, 0.941, 0.984, 1)
        // How much of a bolt's flash reaches the rain. Below 1 on purpose: a whiteout that took
        // the whole screen every 2.5 s would make the storm unplayable to stand in, and the
        // brightness of a flash is not what tells the player they were struck — the damage, the
        // clap and the bolt itself already do (GDC-L1-FEEL-0004, GDC-L1-UX-0006).
        _FlashLift  ("Flash Lift",   Range(0, 1)) = 0.55

        [Header(March)]
        _Steps ("Steps", Range(8, 64)) = 40

        [Header(Life)]
        _Form ("Form", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "StormVeil"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // Cull Front keeps exactly ONE fragment per pixel — the column's far wall — whether
            // the camera is outside the storm or standing in it. With Cull Off, which the surface
            // version needed, a ray through the column raises two fragments and the march would
            // run twice and blend over itself.
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_fog

            #include "StormCloudVolume.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _MeshToUnit;

                float _VeilRadius;
                float _RimFeather;
                float _ClearRadius;

                half4 _RainDark;
                half4 _RainMid;
                half4 _RainLight;
                half4 _SplashTint;

                float _RainDensity;
                float _Streaks;
                float _Columns;
                float _StreakWidth;
                float _ColumnBite;
                float _RainSpeed;
                float _RainTaper;

                float4 _RainWind;
                float _Gust;
                float _GustScale;
                float _GustSpeed;

                float _Murk;
                half4 _MurkTint;
                float _SplashBand;
                float _Splash;

                float _Extinction;
                float _SkyGain;
                float _MaxOpacity;

                float _Flash;
                half4 _FlashColor;
                float _FlashLift;

                float _Steps;
                float _Form;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.fogFactor = ComputeFogFactor(positions.positionCS.z);

                StormCloudPinFarPlane(OUT.positionCS);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                return OUT;
            }

            /// What the rain is doing at one point of the cloud frame.
            ///
            /// `streaks` is the falling water, `murk` is the haze it hangs in. They are returned
            /// separately because they are not the same colour and, more importantly, because
            /// they must not be allowed to substitute for each other: murk alone is fog, streaks
            /// alone is a screensaver, and the whole read comes from having both.
            void VeilDensity(float3 position, float time, out float streaks, out float murk,
                             out float splash)
            {
                streaks = 0.0;
                murk = 0.0;
                splash = 0.0;

                // 0 at the cloud, 1 at the foot of the veil. The mesh defines the veil as one
                // object unit tall, which is what keeps _RainSpeed meaningful in veil lengths a
                // second rather than in a unit nobody can picture.
                float fall = saturate(-position.y);

                // Feathered inward from the authored wall, so the curtain has a shape instead of
                // stopping at a cylinder.
                float radial = length(position.xz) / max(_VeilRadius, 1e-3);
                float inside = 1.0 - smoothstep(1.0 - _RimFeather, 1.0, radial);
                if (inside <= 0.0) return;

                // Travelling sheets of heavier rain. One sine of a dot product; the direction is
                // the wind's, so the surges arrive from where the rain is coming from.
                float2 windDirection = normalize(_RainWind.xz + float2(1e-4, 0.0));
                float gust = 1.0 - _Gust * 0.5
                           * (1.0 - sin(dot(position.xz, windDirection) * _GustScale - time * _GustSpeed));

                // Where this drop STARTED, undoing the sideways drift it has picked up on the way
                // down. Taking the column from the drifted position instead would smear the whole
                // pattern sideways rather than lean the individual streaks.
                float2 source = position.xz - _RainWind.xz * fall;

                // One phase and one weight per column, so neighbouring streaks are neither in
                // lockstep nor equally heavy. Quantized into columns rather than driven
                // continuously, because a continuous phase shears the streaks into a spiral.
                float2 cell = source * _Columns;
                float2 column = floor(cell);
                float phase = SubstanceHash(float3(column, 0.0));
                float weight = 1.0 - _ColumnBite * SubstanceHash(float3(column, 7.3));

                // Each column is a soft ROD, not the cell it was indexed from. Without this the
                // density is constant across the whole cell in both horizontal axes and constant
                // along the ray through it, so the curtain marches as a wall of cubes — the
                // single most obvious way a volumetric rain reads as a bug rather than as rain.
                float2 offset = (cell - column) - 0.5;
                float core = saturate(1.0 - length(offset) * 2.0);
                core *= core;

                float streak = frac(fall * _Streaks - time * _RainSpeed * _Streaks + phase);

                // A hard-ended streak, not a soft blob: rain at this distance is a line, and a
                // soft line is fog. The quantizer's ink pass outlines hard edges for nothing,
                // which is the whole reason this effect can afford to be dark and still be found.
                float body = 1.0 - smoothstep(0.0, _StreakWidth, streak);

                float foot = 1.0 - smoothstep(1.0 - _RainTaper, 1.0, fall);

                streaks = body * weight * core * gust * inside * foot * _RainDensity;
                murk = inside * gust * _Murk;
                splash = _Splash * inside * smoothstep(1.0 - _SplashBand, 1.0, fall);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 screenUv = IN.screenPos.xy / max(1e-4, IN.screenPos.w);

                float3 eye = _WorldSpaceCameraPos;
                float3 rayWS = normalize(IN.positionWS - eye);

                float3 origin = StormCloudToFrame(eye, _MeshToUnit);
                float3 ray = StormCloudDirectionToFrame(rayWS, _MeshToUnit);

                float tNear, tFar;
                if (!StormCloudRayCylinder(origin, ray, _VeilRadius, -1.0, 0.0, tNear, tFar))
                    discard;

                // Standing in the rain is tNear behind the camera. Clamped to zero, the march
                // starts at the eye, which is what makes this the storm's interior.
                tNear = max(tNear, 0.0);
                tFar = StormCloudSceneClamp(screenUv, rayWS, tFar);
                if (tFar <= tNear) discard;

                int steps = (int)_Steps;
                float dt = (tFar - tNear) / steps;
                float t = tNear + dt * InterleavedGradientNoise(IN.positionCS.xy, 0);
                float time = _Time.y;

                // Rain is not lit by the sun it is standing under — it is lit by whatever gets
                // past the cloud, which is the sky's own colour dimmed by the storm. One number,
                // pushed rather than sampled, for the reason StormCloud.shader gives.
                float3 sky = _MainLightColor.rgb * _SkyGain * saturate(_MainLightPosition.y) + 0.02;

                float transmittance = 1.0;
                float3 colour = 0.0;

                for (int i = 0; i < steps; i++)
                {
                    // Held off the camera, so the player can see their own feet and the ground
                    // they are being told is wet. A bound, not a tuning: without it the first
                    // sample is always at zero distance and always at full density.
                    float clear = smoothstep(0.0, max(_ClearRadius, 1e-3), t);
                    if (clear > 0.0)
                    {
                        float3 position = origin + ray * t;

                        float streaks, murk, splash;
                        VeilDensity(position, time, streaks, murk, splash);

                        float density = (streaks + murk + splash) * clear * _Form;
                        if (density > 0.001)
                        {
                            // Banded like every other substance here, and banded on the STREAK
                            // rather than on the total: the flats have to fall on the water, not
                            // on the haze, or the curtain reads as one grey sheet.
                            float3 water = SubstanceLadder3(SubstanceBand(saturate(streaks), 3.0),
                                                            _RainDark.rgb, _RainMid.rgb, _RainLight.rgb);

                            // The splash at the foot is the one part of the veil that is lighter
                            // than what it covers.
                            float3 tint = lerp(_MurkTint.rgb, water, saturate(streaks));
                            tint = lerp(tint, _SplashTint.rgb, saturate(splash));

                            float3 light = tint * sky
                                         + _FlashColor.rgb * _Flash * _FlashLift;

                            float sigma = max(density * _Extinction, 1e-5);
                            float stepTransmittance = exp(-sigma * dt);
                            colour += transmittance * light * (1.0 - stepTransmittance);
                            transmittance *= stepTransmittance;

                            if (transmittance < 0.01) break;
                        }
                    }

                    t += dt;
                }

                float alpha = (1.0 - transmittance) * _MaxOpacity * _Form;
                if (alpha <= 0.003) discard;

                colour = MixFog(colour, IN.fogFactor);
                return half4(colour, saturate(alpha));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
