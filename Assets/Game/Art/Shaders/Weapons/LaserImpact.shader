Shader "SpaceGame/LaserImpact"
{
    // The splash where the Laser Staff's beam meets a surface. A camera-facing quad, drawn
    // additively, carrying a hot radial centre and a ragged corona that boils at the same rate the
    // beam flickers.
    //
    // Separate from LaserBeam rather than folded into it, because the two are the opposite shape:
    // the beam varies along one axis and is uniform around the other, and this varies radially and
    // is uniform along none. Sharing one shader would have meant a branch on which geometry it was
    // drawing, in the fragment stage, forever.
    Properties
    {
        [HDR] _CoreColor  ("Core",   Color) = (1.0, 0.894, 0.894, 1)
        [HDR] _EdgeColor  ("Corona", Color) = (1.0, 0.169, 0.169, 1)

        _Intensity  ("Intensity",        Range(0, 12)) = 4.0
        _CoreSize   ("Core Size",        Range(0.01, 1)) = 0.22
        _Falloff    ("Corona Falloff",   Range(0.5, 8)) = 2.4

        _RayCount   ("Star Ray Count",   Range(0, 16)) = 6
        _RayLength  ("Star Ray Length",  Range(0, 1)) = 0.45
        _Boil       ("Corona Boil",      Range(0, 1)) = 0.35
        _BoilSpeed  ("Corona Boil Speed",Range(0, 40)) = 14

        [Header(Drama)]
        _Spin       ("Star Spin Speed",  Range(-8, 8)) = 1.1
        _Pulse      ("Core Pulse Depth", Range(0, 1)) = 0.45
        _PulseSpeed ("Core Pulse Speed", Range(0, 40)) = 9
        _RingSpeed  ("Shock Ring Speed", Range(0, 8)) = 2.2
        _RingWidth  ("Shock Ring Width", Range(0.01, 0.5)) = 0.09
        _RingBoost  ("Shock Ring Boost", Range(0, 6)) = 1.6
        _Scorch     ("Scorch Darkening", Range(0, 1)) = 0.0
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

        Blend One One
        ZWrite Off
        // Deliberately Always, not LEqual. The quad sits exactly on the surface it is burning, so
        // depth-testing it against that surface makes it z-fight itself into a flickering mess.
        ZTest Always
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
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _EdgeColor;
                float _Intensity;
                float _CoreSize;
                float _Falloff;
                float _RayCount;
                float _RayLength;
                float _Boil;
                float _BoilSpeed;
                float _Spin;
                float _Pulse;
                float _PulseSpeed;
                float _RingSpeed;
                float _RingWidth;
                float _RingBoost;
                float _Scorch;
            CBUFFER_END

            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.uv * 2.0 - 1.0;
                float  r = length(p);

                // Outside the disc there is nothing to draw. Clipping rather than fading to zero
                // keeps the quad's square corners from showing as faint boxes under bloom.
                if (r > 1.0) discard;

                // The core breathes. A cutting beam is not a steady lamp — the metal under it is
                // being blown away and replaced faster than the eye can follow, and a pulsing
                // centre is the cheapest honest way to say so.
                float pulse = 1.0 + _Pulse * sin(_Time.y * _PulseSpeed);

                float core   = pow(saturate(1.0 - r / max(_CoreSize, 1e-4)), 3.0) * pulse;
                float corona = pow(saturate(1.0 - r), _Falloff);

                // Star rays, turning slowly. An impact with a perfectly round corona reads as a
                // decal; the spikes sell it as light scattering in the camera, and spinning them
                // stops the spikes looking welded to the surface as the beam sweeps across it.
                float angle = atan2(p.y, p.x) + _Time.y * _Spin;
                float rays  = pow(saturate(cos(angle * _RayCount) * 0.5 + 0.5), 6.0)
                            * _RayLength * saturate(1.0 - r);

                // A shock ring running outward on a loop, brightest as it leaves the core and gone
                // by the rim. Reuses the SAME phase as nothing else, deliberately: it has to feel
                // independent of the pulse or the two lock together into one throb.
                float ringPhase = frac(_Time.y * _RingSpeed);
                float ring = smoothstep(_RingWidth, 0.0, abs(r - ringPhase))
                           * (1.0 - ringPhase) * _RingBoost;

                float boil = 1.0 - _Boil * hash11(floor(_Time.y * _BoilSpeed));

                float3 colour = _CoreColor.rgb * (core * 1.6)
                              + _EdgeColor.rgb * (corona + rays + ring);

                colour *= _Intensity * boil;

                // Optional soot under the glow. Additive blending cannot darken, so this is
                // expressed as light REMOVED from the corona rather than as black paint — the
                // only way to suggest scorch in a pass that can only add.
                colour *= 1.0 - _Scorch * smoothstep(_CoreSize, 1.0, r);

                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
