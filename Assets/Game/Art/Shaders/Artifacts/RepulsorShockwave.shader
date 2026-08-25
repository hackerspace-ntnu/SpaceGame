Shader "SpaceGame/Artifacts/RepulsorShockwave"
{
    // Expanding ground wave for a repulsor blast. The annulus mesh maps V across the ring width
    // (0 = inner edge, 1 = outer rim) and U around the sweep: a hot leading edge rides the outer
    // rim, a faint trailing skirt falls off behind it, and the whole wave dies out as _Progress
    // reaches 1.
    //
    // The mesh may be a closed 360 ring (a detonation) or an open wedge (a directed blast) — see
    // RepulsorBlastRing. A wedge ends in two straight radial cuts that read as a polygon lying on
    // the sand, so _ArcFade feathers U's two ends; it is left at 0 for a closed ring, where U wraps
    // and the same feather would punch a gap out of one side of the circle.
    Properties
    {
        _Color         ("Color",          Color)         = (0.55, 0.8, 1.0, 1.0)
        _Intensity     ("Intensity",      Range(0, 12))  = 5
        _Progress      ("Progress",       Range(0, 1))   = 0
        _TrailStrength ("Trail Strength", Range(0, 1))   = 0.35
        _EdgeSharpness ("Edge Sharpness", Range(1, 16))  = 6
        _ArcFade       ("Arc End Fade",   Range(0, 1))   = 0
        _ArcFeather    ("Arc Feather",    Range(0.01, 0.5)) = 0.18
        _Turbulence    ("Turbulence",     Range(0, 1))   = 0.45
        _TurbScale     ("Turbulence Scale", Float)       = 26
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One   // additive
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Intensity;
            float _Progress;
            float _TrailStrength;
            float _EdgeSharpness;
            float _ArcFade;
            float _ArcFeather;
            float _Turbulence;
            float _TurbScale;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Cheap value noise. Procedural on purpose: the material ships with no texture, and a
            // one-shot spawned per blast should not drag a texture reference along with it.
            float hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash12(i), hash12(i + float2(1, 0)), f.x),
                            lerp(hash12(i + float2(0, 1)), hash12(i + float2(1, 1)), f.x), f.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float across = saturate(i.uv.y);          // 0 inner edge -> 1 outer rim

                // A high exponent is what keeps the front a THIN bright line instead of a filled
                // disc: the ring has to read as a wave passing over the ground, and a soft wide
                // gradient reads as a light shone on it.
                float lead = pow(across, _EdgeSharpness);
                float trail = across * _TrailStrength;

                // The front breaks up as it travels, so the wave stops looking machined. The noise
                // is sampled along the sweep and scrolled outward with _Progress, which makes the
                // ragged detail travel WITH the front rather than sit still under it.
                float grain = vnoise(float2(i.uv.x * _TurbScale, _Progress * 3.0));
                float ragged = lerp(1.0, 0.45 + 1.1 * grain, _Turbulence);

                float fade = pow(saturate(1.0 - _Progress), 1.5); // wave dies as it lands

                // Feather the two radial cuts of an open wedge. _ArcFade is 0 for a closed ring.
                float ends = smoothstep(0.0, _ArcFeather, i.uv.x)
                           * smoothstep(1.0, 1.0 - _ArcFeather, i.uv.x);
                float arc = lerp(1.0, ends, _ArcFade);

                float a = saturate(lead * ragged + trail) * fade * arc;
                return fixed4(_Color.rgb * _Intensity * a, a * _Color.a);
            }
            ENDCG
        }
    }
    FallBack Off
}
