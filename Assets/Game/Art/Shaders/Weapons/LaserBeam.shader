Shader "SpaceGame/LaserBeam"
{
    // A solid cutting beam for the Laser Staff, drawn on a two-point LineRenderer.
    //
    // The whole look is one idea: the beam is three concentric layers, not one coloured line. A
    // white-hot core with a hard falloff, a crimson body around it, and a wide dark-red halo that
    // reaches well past both. Blended additively, the halo is what makes the beam read as light in
    // the air rather than as a painted stripe, and the core is what survives bloom without going
    // to flat white.
    //
    // The flow ripple scrolls in METRES along the beam, not in UV. A LineRenderer's u runs 0..1
    // from end to end whatever its length, so a UV-space scroll makes a short beam's texture race
    // and a long one's crawl — the same staff would appear to fire two different weapons depending
    // on how far away the wall was. LaserStaffArtifact pushes the world length into _BeamLength
    // each frame and the ripple is measured against that instead.
    Properties
    {
        [Header(Colour)]
        [HDR] _CoreColor ("Core (white hot centre)",  Color) = (1.0, 0.894, 0.894, 1)
        [HDR] _BeamColor ("Beam (crimson body)",      Color) = (1.0, 0.169, 0.169, 1)
        [HDR] _HaloColor ("Halo (deep red bloom)",    Color) = (0.42, 0.0, 0.0, 1)

        _Intensity     ("Intensity",                   Range(0, 12)) = 3.5
        _CoreWidth     ("Core Width (fraction)",       Range(0.01, 1)) = 0.28
        _CoreSharpness ("Core Sharpness",              Range(0.5, 8)) = 2.6
        _HaloFalloff   ("Halo Falloff",                Range(0.1, 4)) = 0.7

        [Header(Flow)]
        _FlowSpeed    ("Flow Speed (m per sec)",       Range(0, 60)) = 18
        _FlowScale    ("Flow Scale (cycles per m)",    Range(0.05, 12)) = 1.8
        _FlowStrength ("Flow Strength",                Range(0, 1)) = 0.35

        [Header(Ends)]
        _MuzzleTaper ("Muzzle Taper",                  Range(0, 0.5)) = 0.06
        _TipFlare    ("Impact Flare Boost",            Range(0, 6)) = 2.2
        _TipWidth    ("Impact Flare Width",            Range(0.01, 0.5)) = 0.12

        [Header(Life)]
        _Flicker      ("Flicker Amount",               Range(0, 1)) = 0.12
        _FlickerSpeed ("Flicker Speed",                Range(0, 90)) = 34
        _Ignite       ("Ignition (0 out, 1 lit)",      Range(0, 1)) = 1
        _BeamLength   ("Beam Length (metres)",         Float) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent"
            "IgnoreProjector" = "True"
        }

        // Additive, and depth-tested but not depth-writing. Testing matters: the beam ends at a
        // wall, and without ZTest the last few centimetres draw over the thing it is supposed to
        // be burning into.
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
                half4 _BeamColor;
                half4 _HaloColor;
                float _Intensity;
                float _CoreWidth;
                float _CoreSharpness;
                float _HaloFalloff;
                float _FlowSpeed;
                float _FlowScale;
                float _FlowStrength;
                float _MuzzleTaper;
                float _TipFlare;
                float _TipWidth;
                float _Flicker;
                float _FlickerSpeed;
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

            // Value noise in one dimension. The beam only ever varies along its own length, so a
            // 2D noise here would be paying for a second axis nothing ever samples.
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
                // visibly repeat over the length of a long beam.
                return noise11(x) * 0.65 + noise11(x * 2.37 + 11.3) * 0.35;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float along  = saturate(IN.uv.x);
                float across = abs(IN.uv.y * 2.0 - 1.0);   // 0 at the centreline, 1 at the edge

                // ── The three layers ──────────────────────────────────────────
                float core = pow(saturate(1.0 - across / max(_CoreWidth, 1e-4)), _CoreSharpness);
                float body = pow(saturate(1.0 - across), 1.6);
                float halo = pow(saturate(1.0 - across), _HaloFalloff);

                // ── Energy travelling along it ────────────────────────────────
                float metres = along * _BeamLength;
                float ripple = flow11(metres * _FlowScale - _Time.y * _FlowSpeed * _FlowScale);
                float flow   = 1.0 + (ripple * 2.0 - 1.0) * _FlowStrength;

                // ── Ends ──────────────────────────────────────────────────────
                // The muzzle taper hides the fact that a LineRenderer starts as a flat cap right
                // inside the fork of the staff; the tip flare is the beam splashing on impact.
                float taper = smoothstep(0.0, max(_MuzzleTaper, 1e-4), along);
                float flare = 1.0 + _TipFlare * smoothstep(1.0 - _TipWidth, 1.0, along);

                // ── Life ──────────────────────────────────────────────────────
                float flicker = 1.0 - _Flicker * hash11(floor(_Time.y * _FlickerSpeed));

                float3 colour =
                      _CoreColor.rgb * core * 1.4
                    + _BeamColor.rgb * body
                    + _HaloColor.rgb * halo * 0.85;

                colour *= _Intensity * flow * taper * flare * flicker;

                // Ignition squared: a beam that brightens linearly out of nothing reads as a lamp
                // being turned up, where a weapon should snap on.
                colour *= _Ignite * _Ignite;

                colour *= IN.color.rgb * IN.color.a;

                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
