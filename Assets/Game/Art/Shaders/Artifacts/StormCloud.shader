// The cloud a Storm Flask uncorks: a small, flat, ANGRY disc of vapour, drawn as a raymarched
// volume inside the body mesh (Artifacts/StormFlask.md).
//
// Everything about the frame, the ray/volume intersections, the turbulence and the two Cull Front
// guards is in StormCloudVolume.hlsl — read that first. This file is the cloud's own shape, its
// lighting and its palette, and nothing else.
//
// ── WHAT MAKES IT READ AS VIOLENT, AND WHY EACH PART IS HERE ────────────────────────────────
//
// The first version of this shader painted the lens as a SURFACE: one churn sample per pixel,
// one depth per pixel. That can only ever simmer. A thing that reads as violent has to have
// interior parallax — near billows crossing far ones as you walk — and that is a property of a
// volume, not of a texture. Four terms do the work, in the order they matter:
//
//   1. DIFFERENTIAL ROTATION (_Spin, _SpinCore). The core turns faster than the rim, so the
//      field is continuously sheared into spirals. Uniform rotation on a field with no landmark
//      in it is invisible; this is not, and it is what says "mesocyclone" rather than "smoke".
//   2. DOMAIN WARP (_Warp). Three noise taps that curl every straight feature downstream of
//      them. Without it the billows are round and the cloud simmers.
//   3. UPDRAFT (_Updraft). The sample point scrolls DOWN, so the cloud boils upward through its
//      own shape — the shape stays put, the vapour does not.
//   4. COVERAGE EROSION (_Coverage). The noise cuts the volume away rather than shading it, so
//      the silhouette itself is ragged and moving. The mesh's own authored rim is deliberately
//      OUTSIDE the analytic bound this marches, because a mesh rim cannot move and this can.
//
// ── THE COVERAGE MASK IS NOT THE DENSITY ────────────────────────────────────────────────────
//
// The shape term is remapped AGAINST the noise (`density = remap(billow, 1 - cover, 1)`), never
// multiplied into it. Used as a density the shape saturates inside its own feather and the cloud
// renders as its bounding solid with a fuzzy rim — the sandstorm shipped that exact picture and
// Environment.md records it. Remapped, the feather is where the cloud goes to WISPS.
//
// ── THE BAND LADDER IS FIVE NOW, AND THAT IS MEASURED ───────────────────────────────────────
//
// The surface version used three bands, because it only ever drew the dark end: below Oklab
// L 0.43 the palette's cold-blue column runs out and a fourth dark step collapses onto the third.
// A lit volume is not in that situation. Its sunlit crown is four lightness rows ABOVE its belly,
// and the column at hue 270 / chroma 0.046 holds exactly five entries across that span —
// #D2D7E5, #B5BDD5, #939EBC, #707996, #464F69 at L 0.88, 0.80, 0.70, 0.58, 0.43 — plus the grey
// ramp's #303030 where the column ends. The defaults below are five of those six, so the
// quantizer's snap is a no-op on every band and the ladder stays as authored (GDC-L1-TECH-0004).
// A bright top over a near-black base is the single strongest cue that a cloud is deep, and the
// three-band version could not have it.
//
// ── AND IT IS STILL A HAZARD TELEGRAPH FIRST ────────────────────────────────────────────────
//
// The bolt falls inside the mesh's rim, and the mesh's rim is what the player is being warned by
// (GDC-L1-UX-0003). So the volume is marched against a bound INSIDE that rim and the wisps erode
// inward from it: what the storm draws is never wider than what the storm bills. A cloud drawn
// generously past its own trigger volume kills people standing outside it, which reads as the
// game cheating.
Shader "SpaceGame/Artifacts/StormCloud"
{
    Properties
    {
        // Multiplies the raw mesh position to reach the unit-radius cloud frame. 100 undoes the
        // centimetre file scale every `_exportlib` FBX carries on its node.
        [Header(Mesh frame)]
        _MeshToUnit ("Mesh Units To Unit Radius", Float) = 100

        // One lattice column, darkest first. See the header: five measured entries, not a ramp
        // between two ends — a lerp is even in linear RGB and the palette's rows are even in
        // Oklab L, so an interpolated ladder bunches its middle bands onto one entry.
        [Header(Cloud bands  darkest first)]
        _BandCore  ("Band 1  Belly",  Color) = (0.188, 0.188, 0.188, 1)
        _BandDeep  ("Band 2  Deep",   Color) = (0.275, 0.310, 0.412, 1)
        _BandMid   ("Band 3  Mid",    Color) = (0.439, 0.475, 0.588, 1)
        _BandLit   ("Band 4  Lit",    Color) = (0.576, 0.620, 0.737, 1)
        _BandCrown ("Band 5  Crown",  Color) = (0.710, 0.741, 0.835, 1)

        // Where the marched radiance is cut into those five bands. The one dial to reach for when
        // the cloud is too dark or washes out; every other lighting number changes the SHAPE of
        // the shading, this one only slides it up and down the ladder.
        _Exposure ("Band Exposure", Range(0.2, 8)) = 0.8

        [Header(Cloud shape)]
        // Inside the mesh's rim, which wanders between 0.72 and 1.00 of the radius. See header.
        _BodyRadius  ("Body Radius",        Range(0.4, 1))    = 0.90
        _BodyTop     ("Body Top",           Range(0.1, 0.8))  = 0.28
        _Density     ("Density",            Range(0.1, 6))    = 2.2
        _Coverage    ("Coverage",           Range(0.1, 1))    = 0.62
        _RimStart    ("Rim Falloff Start",  Range(0, 0.95))   = 0.45
        _CrownStart  ("Crown Taper Start",  Range(0, 0.95))   = 0.35
        _BellyWeight ("Belly Weight",       Range(0, 2))      = 0.75
        _ChurnScale  ("Churn Scale",        Range(0.5, 12))   = 3.4

        [Header(Violence)]
        _Spin      ("Spin (rad per s)",   Range(0, 2))   = 0.35
        _SpinCore  ("Spin Core Boost",    Range(0, 4))   = 1.8
        _Warp      ("Domain Warp",        Range(0, 1))   = 0.34
        _WarpScale ("Domain Warp Scale",  Range(0.2, 6)) = 1.5
        _WarpDrift ("Domain Warp Drift",  Range(0, 2))   = 0.16
        _Updraft   ("Updraft (units per s)", Range(0, 1)) = 0.13

        [Header(Light)]
        // Pushed as a property rather than read from SampleSH. The sandstorm found SH returning
        // zero in both of its paths — a procedural blit has no per-draw SH constants, and its
        // shell renderer had light probes off — and the storm rendered black with a clean
        // console. A number that is visible in the inspector cannot fail that quietly.
        _SkyColor    ("Sky Light",           Color)          = (0.741, 0.816, 0.898, 1)
        _SkyGain     ("Sky Gain",            Range(0, 4))    = 0.70
        _SunGain     ("Sun Gain",            Range(0, 20))   = 4.0
        _Anisotropy  ("Forward Scatter",     Range(-0.9, 0.9)) = 0.42
        // How much of the sun still reaches the cloud's UNDERSIDE. The three-tap sun march
        // cannot answer this on its own: the body is only about three and a half metres thick,
        // so a march long enough to leave it accumulates almost no depth and the belly comes out
        // as bright as the crown — which is the one thing a storm cloud must never look like.
        // A flat cloud's base is in the shadow of the whole cloud above it, and this is that.
        _BellyShadow ("Belly Shadow",        Range(0, 1))    = 0.10
        _Extinction  ("Extinction (per m)",  Range(0.01, 2)) = 0.55

        [Header(Lightning)]
        _Flash      ("Flash",                Range(0, 1))    = 0
        _FlashColor ("Flash Colour",         Color)          = (0.898, 0.941, 0.984, 1)
        // Where the bolt left, in WORLD space, pushed by StormCloudLook from the replicated
        // strike. w is unused; it is a Vector so one SetVector carries the point.
        _BoltPoint  ("Bolt Point (world)",   Vector)         = (0, 0, 0, 0)
        _BoltReach  ("Bolt Reach (m)",       Range(1, 40))   = 9

        [Header(March)]
        _Steps      ("Steps",                Range(8, 64))   = 28
        _LightSteps ("Sun Steps",            Range(1, 6))    = 3
        _SunStep    ("Sun Step (m)",         Range(0.2, 8))  = 0.8
        // BOUNDED, never integrated to opacity. A Beer march always reaches alpha 1 given enough
        // distance, so without a cap the middle of the disc is a flat silhouette with no
        // structure in it — the same reason the sandstorm's interior carries a maxFogOpacity.
        _MaxOpacity ("Max Opacity",          Range(0.1, 1))  = 0.97

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
            Name "StormCloud"

            // Straight alpha, not additive: a storm cloud takes light away, and additive is what
            // makes a dark cloud impossible — the brightest thing behind it is the sky, which is
            // exactly what it has to be darker than.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // The fragment is the volume's FAR side, so the march always has the whole volume in
            // front of it whether the camera is outside the cloud or inside it. Depth is settled
            // by the depth TEXTURE instead — see StormCloudVolume.hlsl.
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

                half4 _BandCore;
                half4 _BandDeep;
                half4 _BandMid;
                half4 _BandLit;
                half4 _BandCrown;
                float _Exposure;

                float _BodyRadius;
                float _BodyTop;
                float _Density;
                float _Coverage;
                float _RimStart;
                float _CrownStart;
                float _BellyWeight;
                float _ChurnScale;

                float _Spin;
                float _SpinCore;
                float _Warp;
                float _WarpScale;
                float _WarpDrift;
                float _Updraft;

                half4 _SkyColor;
                float _SkyGain;
                float _SunGain;
                float _Anisotropy;
                float _BellyShadow;
                float _Extinction;

                float _Flash;
                half4 _FlashColor;
                float4 _BoltPoint;
                float _BoltReach;

                float _Steps;
                float _LightSteps;
                float _SunStep;
                float _MaxOpacity;

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

            /// How much cloud is at one point of the cloud frame, and how high up the body it is.
            float CloudDensity(float3 position, float time, out float heightFraction)
            {
                heightFraction = saturate(position.y / max(_BodyTop, 1e-3));
                float radial = length(position.xz) / max(_BodyRadius, 1e-3);

                // Hard at the base, tapering to the crown, feathered at the rim. The flat dark
                // underside is the design's own read — it is the face a bolt comes out of, and
                // the face the player is standing under when it matters.
                // NO FEATHER AT THE BASE, deliberately. A cumulonimbus has a flat dark
                // underside, the design calls for one, and a feather there opens a bright gap
                // of sky between the cloud's belly and the top of the rain veil — the two
                // meshes meet at exactly y = 0 and the veil has no geometry above it to close
                // the seam from its side.
                float shape = (1.0 - smoothstep(_RimStart, 1.0, radial))
                            * (1.0 - smoothstep(_CrownStart, 1.0, heightFraction));
                if (shape <= 0.0) return 0.0;

                float3 sample = StormCloudSpin(position, _Spin, _SpinCore, time);
                sample = StormCloudWarp(sample, _WarpScale, _Warp, time * _WarpDrift);
                sample.y -= time * _Updraft;

                float billow = StormCloudBillow(sample * _ChurnScale);

                // Coverage, not density. See the file header.
                float cover = saturate(shape * _Coverage * _Form);
                float density = saturate((billow - (1.0 - cover)) / max(cover, 1e-3));

                return density * _Density * (1.0 + _BellyWeight * (1.0 - heightFraction));
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 screenUv = IN.screenPos.xy / max(1e-4, IN.screenPos.w);

                float3 eye = _WorldSpaceCameraPos;
                float3 rayWS = normalize(IN.positionWS - eye);

                // Into the cloud frame. The direction was normalised in WORLD space first, so
                // every `t` below is still a distance in metres — see StormCloudVolume.hlsl.
                float3 origin = StormCloudToFrame(eye, _MeshToUnit);
                float3 ray = StormCloudDirectionToFrame(rayWS, _MeshToUnit);

                float tNear, tFar;
                if (!StormCloudRayCylinder(origin, ray, _BodyRadius, 0.0, _BodyTop, tNear, tFar))
                    discard;

                tNear = max(tNear, 0.0);
                tFar = StormCloudSceneClamp(screenUv, rayWS, tFar);
                if (tFar <= tNear) discard;

                float3 sunDirectionWS = normalize(_MainLightPosition.xyz);
                float3 sunDirection = StormCloudDirectionToFrame(sunDirectionWS, _MeshToUnit);
                float3 sunColor = _MainLightColor.rgb;

                // The sun's own elevation, not a fudge factor. It is how much of the sun falls on
                // the top of the storm, and it is what gives the cloud a day-night response for
                // free; the sandstorm's hand-picked 0.35 gave noon exactly the light of sunset.
                float sunElevation = saturate(sunDirectionWS.y);
                float phase = StormCloudPhase(dot(rayWS, sunDirectionWS), _Anisotropy);

                int steps = (int)_Steps;
                int lightSteps = (int)_LightSteps;
                float dt = (tFar - tNear) / steps;

                // Jittered start, so the march's own step lands on a different depth per pixel.
                // Unjittered, a volume this shallow bands into visible shells.
                float t = tNear + dt * InterleavedGradientNoise(IN.positionCS.xy, 0);
                float time = _Time.y;

                float transmittance = 1.0;
                float3 scatter = 0.0;
                float boltPeak = 0.0;

                for (int i = 0; i < steps; i++)
                {
                    float3 position = origin + ray * t;
                    float heightFraction;
                    float density = CloudDensity(position, time, heightFraction);

                    if (density > 0.001)
                    {
                        // Toward the sun, in metres: sunDirection is the cloud-frame image of a
                        // unit world vector, so a step of _SunStep along it is _SunStep metres.
                        float sunDepth = 0.0;
                        for (int j = 0; j < lightSteps; j++)
                        {
                            float3 lightSample = position + sunDirection * _SunStep * (j + 0.5);
                            float ignored;
                            sunDepth += CloudDensity(lightSample, time, ignored) * _SunStep;
                        }

                        float3 positionWS = eye + rayWS * t;
                        float bolt = StormCloudBoltGlow(positionWS, _BoltPoint.xyz, _Flash, _BoltReach);
                        boltPeak = max(boltPeak, bolt);

                        float3 light =
                            sunColor * _SunGain * sunElevation * phase
                                     * StormCloudSunTransmittance(sunDepth, _Extinction)
                                     * lerp(_BellyShadow, 1.0, heightFraction)
                          + _SkyColor.rgb * _SkyGain * (0.30 + 0.70 * heightFraction)
                          + _FlashColor.rgb * bolt;

                        // Analytic integration over the step rather than a Riemann sum, so the
                        // result does not change brightness when _Steps is changed.
                        float sigma = max(density * _Extinction, 1e-5);
                        float stepTransmittance = exp(-sigma * dt);
                        scatter += transmittance * light * (1.0 - stepTransmittance);
                        transmittance *= stepTransmittance;

                        if (transmittance < 0.01) break;
                    }

                    t += dt;
                }

                float alpha = (1.0 - transmittance) * _MaxOpacity * _Form;
                if (alpha <= 0.003) discard;

                // Banded, not ramped: the quantizer would band it anyway, and an authored ladder
                // is one that can be tuned. Luminance weights are Rec. 709 on linear values.
                float tone = saturate(dot(scatter, float3(0.2126, 0.7152, 0.0722)) * _Exposure);
                float3 colour = SubstanceLadder5(tone, _BandCore.rgb, _BandDeep.rgb, _BandMid.rgb,
                                                 _BandLit.rgb, _BandCrown.rgb);

                // The bolt's own whiteout goes on AFTER the ladder and only at its peak, so a
                // strike is allowed off the palette for the fraction of a second it lasts. Every
                // other frame of the cloud's life is on it.
                colour = lerp(colour, _FlashColor.rgb, saturate(boltPeak));

                colour = MixFog(colour, IN.fogFactor);
                return half4(colour, saturate(alpha));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
