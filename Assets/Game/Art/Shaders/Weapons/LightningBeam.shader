Shader "SpaceGame/LightningBeam"
{
    // The Laser Staff's beam, drawn as a lightning arc rather than as a laser.
    //
    // It replaced SpaceGame/LaserBeam, and the difference is not a palette swap. A laser is a
    // straight line whose brightness is constant along it; an arc is a discharge that re-strikes
    // several times a second, breaks into segments, and is far brighter at its filament than
    // anything around it. Two halves produce that, and only one of them lives here:
    //
    //   • The SHAPE — the kinks and the sway — is geometry. LaserStaffArtifact feeds the
    //     LineRenderer a couple of dozen points and displaces them sideways each frame. A shader
    //     cannot do it: a fragment shader may darken a pixel but it cannot move the ribbon, so a
    //     "bolt" faked in UV is always a painted squiggle sitting on a straight strip, and it
    //     reads as one the moment the beam sweeps.
    //   • The DISCHARGE — the filament, the segment breaks, the strobe — is this shader.
    //
    // Red, deliberately and only. The three colours below are a single hue at three exposures, so
    // there is no second hue anywhere in the beam: the centre is hot enough to bloom towards white
    // on its own without ever being authored as white.
    //
    // Flow and breakup scroll in METRES, not in UV. A LineRenderer's u runs 0..1 from end to end
    // whatever its length, so a UV-space rate makes a short beam crackle furiously and a long one
    // crawl — the same staff would look like two different weapons depending on how far away the
    // wall is. LaserStaffArtifact pushes the world length into _BeamLength every frame.
    Properties
    {
        [Header(Colour   red only)]
        [HDR] _CoreColor ("Core (filament)",     Color) = (1.0, 0.30, 0.24, 1)
        [HDR] _BoltColor ("Bolt (body)",         Color) = (1.0, 0.09, 0.06, 1)
        [HDR] _GlowColor ("Glow (deep red air)", Color) = (0.45, 0.015, 0.0, 1)

        _Intensity     ("Intensity",              Range(0, 12)) = 4.2
        _CoreWidth     ("Core Width (fraction)",  Range(0.01, 1)) = 0.14
        _CoreSharpness ("Core Sharpness",         Range(0.5, 8)) = 3.4
        _GlowFalloff   ("Glow Falloff",           Range(0.1, 4)) = 0.55

        [Header(Discharge)]
        _CrackleScale  ("Crackle Scale (cycles per m)", Range(0.1, 30)) = 6.0
        _CrackleSpeed  ("Crackle Speed (m per sec)",    Range(0, 90)) = 34
        _CrackleDepth  ("Crackle Depth",                Range(0, 1)) = 0.55
        _StrikeRate    ("Re-strike Rate (per sec)",     Range(1, 60)) = 22
        _StrikeDepth   ("Re-strike Depth",              Range(0, 1)) = 0.35

        [Header(Ends)]
        _MuzzleTaper ("Muzzle Taper",       Range(0, 0.5)) = 0.05
        _TipFlare    ("Impact Flare Boost", Range(0, 6)) = 2.6
        _TipWidth    ("Impact Flare Width", Range(0.01, 0.5)) = 0.14

        [Header(Life)]
        _Ignite     ("Ignition (0 out, 1 lit)", Range(0, 1)) = 1
        _BeamLength ("Beam Length (metres)",    Float) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
        }

        // Additive, depth-tested but not depth-writing. Testing matters: the arc ends at a wall,
        // and without ZTest its last few centimetres draw over the thing it is striking.
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half4  color       : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _BoltColor;
                half4 _GlowColor;
                float _Intensity;
                float _CoreWidth;
                float _CoreSharpness;
                float _GlowFalloff;
                float _CrackleScale;
                float _CrackleSpeed;
                float _CrackleDepth;
                float _StrikeRate;
                float _StrikeDepth;
                float _MuzzleTaper;
                float _TipFlare;
                float _TipWidth;
                float _Ignite;
                float _BeamLength;
            CBUFFER_END

            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            // Value noise in one dimension. The arc only varies along its own length, so a 2D noise
            // here would be paying for a second axis nothing ever samples.
            float noise11(float x)
            {
                float i = floor(x);
                float f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(hash11(i), hash11(i + 1.0), f);
            }

            float flow11(float x)
            {
                // Two octaves, the second at an irrational-ish multiple so the pattern does not
                // visibly repeat over the length of a long arc.
                return noise11(x) * 0.65 + noise11(x * 2.37 + 11.3) * 0.35;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float along  = saturate(IN.uv.x);
                float across = abs(IN.uv.y * 2.0 - 1.0);   // 0 at the centreline, 1 at the edge

                // ── The two layers ────────────────────────────────────────────
                // A discharge has a filament and the air around it, and almost nothing between —
                // which is why the core is far narrower and sharper here than a laser's, and the
                // glow far wider. The gap is the look.
                float core = pow(saturate(1.0 - across / max(_CoreWidth, 1e-4)), _CoreSharpness);
                float glow = pow(saturate(1.0 - across), _GlowFalloff);

                // ── Segment breakup travelling along it ───────────────────────
                float metres  = along * _BeamLength;
                float ripple  = flow11(metres * _CrackleScale - _Time.y * _CrackleSpeed * _CrackleScale);
                float crackle = 1.0 - _CrackleDepth * (1.0 - ripple);

                // ── Re-strike ─────────────────────────────────────────────────
                // Quantised, not smooth. An arc is a sequence of separate discharges, so the whole
                // bolt jumps to a new brightness at a fixed rate; a sine here would read as a lamp
                // breathing. The floor is what makes it snap.
                float strike = 1.0 - _StrikeDepth * hash11(floor(_Time.y * _StrikeRate));

                // ── Ends ──────────────────────────────────────────────────────
                // The muzzle taper hides the flat cap a LineRenderer starts with, right inside the
                // fork of the staff; the tip flare is the discharge earthing into what it hits.
                float taper = smoothstep(0.0, max(_MuzzleTaper, 1e-4), along);
                float flare = 1.0 + _TipFlare * smoothstep(1.0 - _TipWidth, 1.0, along);

                float3 colour =
                      _CoreColor.rgb * core * 1.6
                    + _BoltColor.rgb * core * 0.6
                    + _GlowColor.rgb * glow * 0.9;

                // Crackle dims the body but never the filament: a discharge breaking into segments
                // still has an unbroken channel down its middle, and dimming that too turns the
                // beam into a dashed line with gaps you can see the wall through.
                colour *= lerp(crackle, 1.0, core);
                colour *= _Intensity * strike * taper * flare;

                // Ignition squared: a beam that brightens linearly out of nothing reads as a lamp
                // being turned up, where a weapon should snap on.
                colour *= _Ignite * _Ignite;

                colour *= IN.color.rgb * IN.color.a;

                return half4(colour, 1.0);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
