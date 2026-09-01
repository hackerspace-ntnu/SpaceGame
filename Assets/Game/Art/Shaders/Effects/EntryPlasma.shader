// The plasma sheath that wraps the lander while it burns through the atmosphere.
//
// Drawn on the BACK faces of an ellipsoid shell that encloses the hull, so from a seat you are
// inside it looking out and from outside the ship you see it wrapped behind the silhouette. The
// mesh is only a proxy for where on screen the burn might be — every pixel decides its own colour
// from its direction on that shell in the SHIP'S object space, which is what keeps the hot cap
// pinned to the nose and the wake trailing aft however the head turns.
//
// WHY IT IS ONLY VISIBLE THROUGH THE WINDOW, with no mask and no stencil: this draws with an
// ordinary ZTest LEqual against the depth the opaque pass already wrote. The cabin walls are
// opaque and two metres away, so they reject the shell twenty-odd metres behind them; the canopy
// dome is transparent with ZWrite off (PlayerShipBuilder.MakeCanopyGlass) and writes no depth at
// all, so the burn survives exactly across the glass. The same test is what makes the ship
// silhouette itself correctly against its own plasma when seen from outside.
//
// Queued ahead of Transparent so the canopy's own tint lands ON the burn rather than under it —
// render queue is the primary sort key, distance only breaks ties within a queue, and a shell that
// encloses the camera cannot be distance-sorted against the glass it is seen through.
//
// _Flicker is the whole-sheath luminance, driven from the CPU rather than sampled here. EntryBurn
// computes it once per frame and hands the SAME number to this shader and to the cabin glow lamp,
// which is what keeps the light inside the cabin in phase with the fire outside it. Two noises on
// two clocks read as two unrelated faults.
Shader "SpaceGame/Effects/EntryPlasma"
{
    Properties
    {
        _CoreColor  ("Core Colour",  Color) = (1.0, 0.93, 0.72, 1)
        _EdgeColor  ("Edge Colour",  Color) = (1.0, 0.36, 0.06, 1)
        _DeepColor  ("Deep Colour",  Color) = (0.40, 0.05, 0.01, 1)

        _Intensity  ("Intensity",    Range(0, 1)) = 0
        _Flicker    ("Flicker Level",Range(0, 2)) = 1
        _Brightness ("Brightness",   Range(0, 16)) = 3.4

        _NoseBias   ("Nose Bias",       Range(-1, 1))  = -0.15
        _TailStrength ("Tail Strength", Range(0, 1))   = 0.18
        _EdgeFade   ("Silhouette Fade", Range(0.02, 1)) = 0.3

        _StreakScale   ("Streak Scale",   Range(1, 40)) = 9
        _StreakStretch ("Streak Stretch", Range(0.02, 1)) = 0.16
        _FlowSpeed     ("Flow Speed",     Range(0, 12)) = 3.2
        _Contrast      ("Streak Contrast",Range(0.5, 6)) = 3.6

        _EmberThreshold  ("Ember Threshold",  Range(0, 0.99)) = 0.78
        _EmberBrightness ("Ember Brightness", Range(0, 4))    = 1.6
        _EmberScale      ("Ember Scale",      Range(4, 120))  = 42
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent-100"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }

        Pass
        {
            Name "EntryPlasma"

            Blend SrcAlpha One   // additive: fire adds light, it never takes any away
            ZWrite Off
            ZTest LEqual         // the cabin's own opaque walls are the mask. See the header.
            Cull Front           // we are inside the shell; the far wall is what we look at

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _EdgeColor;
                float4 _DeepColor;
                float _Intensity;
                float _Flicker;
                float _Brightness;
                float _NoseBias;
                float _TailStrength;
                float _EdgeFade;
                float _StreakScale;
                float _StreakStretch;
                float _FlowSpeed;
                float _Contrast;
                float _EmberThreshold;
                float _EmberBrightness;
                float _EmberScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS      : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 viewWS     : TEXCOORD2;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                OUT.positionCS = TransformWorldToHClip(positionWS);

                // Object space, not world: the shell mesh is a unit sphere and the ship's +Z is its
                // nose, so this direction is "where on the sheath am I" in a frame that turns with
                // the hull. Taken in world space instead, the hot cap would stay pointing at a
                // compass bearing while the ship pitched over into its dive.
                OUT.dirOS = IN.positionOS.xyz;

                // For the silhouette fade. Through the inverse transpose, because the shell is
                // scaled hard along Z — a normal carried through the plain object-to-world matrix
                // would lean the fade toward the nose and put the seam back somewhere else.
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewWS = GetWorldSpaceViewDir(positionWS);
                return OUT;
            }

            // Cheap 3D value noise. Procedural on purpose: this material is created by the ship
            // builder and a texture reference would be one more thing a rebuild could drop.
            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float VNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash13(i + float3(0, 0, 0));
                float n100 = Hash13(i + float3(1, 0, 0));
                float n010 = Hash13(i + float3(0, 1, 0));
                float n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1));
                float n101 = Hash13(i + float3(1, 0, 1));
                float n011 = Hash13(i + float3(0, 1, 1));
                float n111 = Hash13(i + float3(1, 1, 1));

                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);

                return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
            }

            // Three octaves, not six. The look asked for is tongues of flame, not smoke: the fine
            // octaves that make a plume look soft are exactly what stops a streak reading as one
            // continuous thing travelling past the glass.
            float Fbm(float3 p)
            {
                float v = 0.5 * VNoise(p);
                v += 0.28 * VNoise(p * 2.03 + 11.7);
                v += 0.14 * VNoise(p * 4.11 + 27.3);
                return v / 0.92;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Renormalised per pixel: interpolating across a triangle of the sphere gives a
                // point slightly inside it, and an un-normalised direction would band the cap.
                float3 n = normalize(IN.dirOS);

                // +1 dead ahead of the nose, -1 straight out the back.
                float flow = n.z;

                // The stagnation cap: hot where the hull meets the air, falling away aft.
                float cap = smoothstep(_NoseBias, 1.0, flow);

                // A dim sheath that wraps the whole hull, so the wake fades out instead of ending
                // on a hard rim halfway down the fuselage.
                float wrap = smoothstep(-1.0, _NoseBias, flow) * _TailStrength;

                // Streaks. The sample coordinate is SQUASHED along the flight axis (so one cell of
                // noise spans a long stretch of it, which is what makes a streak a streak rather
                // than a blob) and scrolled toward -Z, so the flow visibly runs past the window
                // from the nose backwards.
                float3 q = n * _StreakScale;
                q.z = q.z * _StreakStretch - _Time.y * _FlowSpeed;

                // No gain on the way out. Multiplied back up, the noise saturates almost everywhere
                // and the sheath becomes one continuous sheet — which through a window reads as a
                // screen effect painted over the glass rather than as fire the ship is flying
                // through. The gaps between the streaks are the effect.
                float streak = pow(saturate(Fbm(q)), _Contrast);

                float heat = saturate(cap * streak + wrap);

                // Fade out where the sight-line grazes the shell. Without this the ellipsoid's own
                // silhouette is drawn — a hard elliptical rim hanging in the air around the ship,
                // which is the mesh admitting it is a mesh. Head-on the fade is 1 and costs
                // nothing, so the sheath keeps its full strength exactly where it is being looked
                // through.
                float facing = abs(dot(normalize(IN.viewWS), normalize(IN.normalWS)));
                float silhouette = smoothstep(0.0, _EdgeFade, facing);

                // Flecks of ablated hull streaming aft: the same flow, sampled far finer and
                // thresholded so only the top of the noise survives as a spark.
                float3 e = n * _EmberScale;
                e.z = e.z * _StreakStretch - _Time.y * _FlowSpeed * 1.7;

                float sparkNoise = VNoise(e);
                float spark = saturate(sparkNoise - _EmberThreshold) / max(1e-4, 1.0 - _EmberThreshold);
                // Sparks only exist where there is fire to shed them from.
                float ember = pow(spark, 6.0) * _EmberBrightness * cap;

                // Two-tone ramp, deliberately bold: deep red body, orange through the middle, and
                // a near-white core only at the very top of the heat.
                float3 col = lerp(_DeepColor.rgb, _EdgeColor.rgb, saturate(heat * 1.8));
                // A high exponent and no gain: the near-white core is meant to be the top of the
                // fire and nothing else. Lifted, it takes over the whole sheath and every streak
                // blows out to the same white, which loses the colour the ramp exists for.
                col = lerp(col, _CoreColor.rgb, saturate(pow(heat, 3.5)));
                col += _CoreColor.rgb * ember;

                float a = saturate((heat + ember) * _Flicker * _Intensity) * silhouette;

                return half4(col * _Brightness, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
