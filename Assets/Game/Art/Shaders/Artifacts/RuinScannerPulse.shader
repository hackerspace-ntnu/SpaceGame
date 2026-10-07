Shader "SpaceGame/RuinScannerPulse"
{
    // Soft cone of light for the Ruin Scanner. A real 3D cone mesh, drawn
    // additively so it reads as a translucent volume of blue light reaching
    // from the scanner muzzle out to the scanned ground.
    //
    // _ConeAlpha is the per-fragment opacity — keep it low (≈0.01) for the
    // "99% transparent" look. The cone is double-sided (Cull Off), so a view
    // ray crosses two walls, stacking the fill into a soft volumetric haze.
    Properties
    {
        _Color       ("Color",                          Color)         = (0.45, 0.95, 1.0, 1.0)
        _ConeAlpha   ("Cone Fill Alpha",                 Range(0, 1))   = 0.04
        _EdgeSoft    ("Edge Softness (radial falloff)",  Range(0, 1))   = 0.35
        _TipFade     ("Tip Fade (near muzzle)",          Range(0, 1))   = 0.15
        _Progress    ("Progress",                        Range(0, 1))   = 0

        [Header(Holographic)]
        _ScanlineFreq  ("Scanline Frequency",   Range(0, 200))  = 60
        _ScanlineSpeed ("Scanline Scroll Speed",Range(-8, 8))   = 2.5
        _ScanlineDepth ("Scanline Contrast",    Range(0, 1))    = 0.6
        _PulseSpeed    ("Travelling Pulse Speed",Range(0, 8))   = 1.5
        _PulseWidth    ("Travelling Pulse Width",Range(0.02, 1))= 0.18
        _PulseBoost    ("Travelling Pulse Boost",Range(0, 4))   = 1.6
        _Flicker       ("Flicker Amount",       Range(0, 1))    = 0.35
        _FlickerSpeed  ("Flicker Speed",        Range(0, 60))   = 22
        _Jitter        ("Scanline Jitter",      Range(0, 0.3))  = 0.06
        _RimGlow       ("Edge Rim Glow",        Range(0, 4))    = 1.5
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha One   // additive

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                // x = radial distance from cone axis [0..1], y = depth [0=tip, 1=base]
                float2 uv  : TEXCOORD0;
            };

            fixed4 _Color;
            float  _ConeAlpha;
            float  _EdgeSoft;
            float  _TipFade;
            float  _Progress;

            float  _ScanlineFreq;
            float  _ScanlineSpeed;
            float  _ScanlineDepth;
            float  _PulseSpeed;
            float  _PulseWidth;
            float  _PulseBoost;
            float  _Flicker;
            float  _FlickerSpeed;
            float  _Jitter;
            float  _RimGlow;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            // Cheap hash for jitter / flicker noise.
            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;

                // Soft radial falloff so the cone rim fades instead of ending
                // in a hard edge.
                float radial = saturate(1.0 - smoothstep(1.0 - _EdgeSoft, 1.0, i.uv.x));
                // Fade in from the tip so the muzzle itself isn't washed out.
                float tip    = smoothstep(0.0, _TipFade, i.uv.y);
                // Ease the pulse in and out over its lifetime.
                float life   = smoothstep(0.0, 0.08, _Progress)
                             * smoothstep(1.0, 0.85, _Progress);

                // ---- Holographic processing ----

                // Jitter the scanline coordinate per-band so rows wobble like
                // an unstable hologram.
                float band   = floor(i.uv.y * _ScanlineFreq);
                float jit    = (hash11(band + floor(t * 9.0)) - 0.5) * 2.0 * _Jitter;
                float scanY  = i.uv.y + jit;

                // Scrolling scanlines along the cone depth.
                float scan   = sin((scanY * _ScanlineFreq) - t * _ScanlineSpeed);
                float scanline = 1.0 - _ScanlineDepth * (0.5 - 0.5 * scan);

                // A bright band travelling tip → base and looping.
                float pulsePos  = frac(t * _PulseSpeed);
                float pulseDist = abs(i.uv.y - pulsePos);
                float pulse     = pow(saturate(1.0 - pulseDist / _PulseWidth), 2.0) * _PulseBoost;

                // Global flicker — a slightly noisy brightness wobble.
                float fseed   = floor(t * _FlickerSpeed);
                float flicker = 1.0 - _Flicker * hash11(fseed);

                // Volume fill: scanlines + flicker + travelling pulse, all
                // gated by the radial falloff so the cone interior glows.
                float fill = (scanline * flicker + pulse) * radial;

                // Rim glow: a crisp bright edge so the cone silhouette reads
                // as a contained holographic field. Lives outside the radial
                // falloff (which is near-zero at the rim) and gets its own
                // flicker so the outline shimmers.
                float rim = smoothstep(1.0 - _EdgeSoft, 1.0, i.uv.x)
                          * _RimGlow * flicker;

                float holo = fill + rim;

                float a = saturate(_ConeAlpha * tip * life * holo);
                return fixed4(_Color.rgb, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
