Shader "SpaceGame/ItemScannerScreen"
{
    // The item scanner's display, drawn entirely in the fragment shader: no
    // render texture, no canvas, no font asset, one draw call on one quad.
    //
    // That choice is the whole design. A world-space canvas on a held item has
    // to be rebuilt every time a blip moves, gets its own camera-facing and
    // sorting problems, and puts a UI hierarchy inside a prefab that is
    // instantiated and destroyed on every hotbar switch. A shader has none of
    // those: the CPU pushes an array of contacts into a MaterialPropertyBlock
    // and the GPU draws the whole instrument.
    //
    // Everything is authored in the 0..1 UV space of the screen plate, which is
    // why that plate is the one mesh in the model library carrying UVs.
    //
    // The readouts are seven-segment digits assembled from rectangles — a real
    // font would need an atlas, and a CRT instrument does not have one anyway.
    Properties
    {
        _Phosphor    ("Phosphor",             Color)        = (0.42, 1.0, 0.60, 1)
        _Deep        ("Tube Base",            Color)        = (0.02, 0.075, 0.045, 1)
        [Header(Live state driven from ItemScannerScreen)]
        // Aspect and Flip X are measured off the plate's own UVs and transform
        // every frame, so the values below are only what a preview shows before
        // anything drives them.
        _Aspect      ("Aspect (w / h)",       Float)        = 1.23
        _FlipX       ("Flip X",               Range(0, 1))  = 0
        _Power       ("Power 0..1",           Range(0, 1))  = 1
        _Sweep       ("Sweep 0..1",           Range(0, 1))  = 0
        _BlipCount   ("Blip Count",           Float)        = 0
        _Contacts    ("Contacts Total",       Float)        = 0
        _Nearest     ("Nearest Metres",       Float)        = 0
        _RangeM      ("Range Metres",         Float)        = 50

        [Header(Tube character)]
        _Gain        ("Beam Gain",            Range(0.2, 4))    = 1.35
        _ScanlineFreq("Scanline Frequency",   Range(0, 400))    = 190
        _ScanlineDepth("Scanline Contrast",   Range(0, 1))      = 0.30
        _Flicker     ("Flicker",              Range(0, 1))      = 0.10
        _Curve       ("Tube Curvature",       Range(0, 0.4))    = 0.13
        _Vignette    ("Vignette",             Range(0, 2))      = 0.85
        _Noise       ("Snow",                 Range(0, 1))      = 0.06
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            // Must match ItemScannerScreen.MaxBlips. A MaterialPropertyBlock
            // array is sized by the first SetVectorArray it receives, so the
            // driver always submits exactly this many entries, padded.
            #define MAX_BLIPS 24

            #define PI 3.14159265

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            fixed4 _Phosphor;
            fixed4 _Deep;
            float  _Aspect;
            float  _FlipX;
            float  _Power;
            float  _Sweep;
            float  _BlipCount;
            float  _Contacts;
            float  _Nearest;
            float  _RangeM;
            float  _Gain;
            float  _ScanlineFreq;
            float  _ScanlineDepth;
            float  _Flicker;
            float  _Curve;
            float  _Vignette;
            float  _Noise;

            // xy = contact in scanner space, normalised by range: x across
            // (-1 left .. +1 right), y forward (1 = full range ahead, negative
            // = behind the holder). z = freshness 0..1. w = ScanClass.
            float4 _Blips[MAX_BLIPS];

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // -- primitives -------------------------------------------------

            float bar(float2 p, float2 c, float2 h)
            {
                float2 d = abs(p - c) - h;
                return step(max(d.x, d.y), 0.0);
            }

            float line_at(float v, float target, float w)
            {
                return 1.0 - smoothstep(0.0, w, abs(v - target));
            }

            /// Which of the seven segments digit `v` lights, as a bitfield.
            ///
            /// A comparison chain rather than a lookup table: a local array
            /// indexed by a value the compiler cannot fold is the one construct
            /// in this shader that would compile on some targets and not
            /// others, and a display that renders on Metal but not on D3D is
            /// worse than a slightly longer function.
            int seg_mask(int v)
            {
                if (v == 0) return 63;
                if (v == 1) return 6;
                if (v == 2) return 91;
                if (v == 3) return 79;
                if (v == 4) return 102;
                if (v == 5) return 109;
                if (v == 6) return 125;
                if (v == 7) return 7;
                if (v == 8) return 127;
                return 111;
            }

            /// One seven-segment digit inside the unit box `p`.
            float digit(float2 p, int value)
            {
                if (p.x < 0.0 || p.x > 1.0 || p.y < 0.0 || p.y > 1.0) return 0.0;

                int m = seg_mask(clamp(value, 0, 9));

                float t = 0.075;
                float on = 0.0;
                on = max(on, ((m /   1) % 2) * bar(p, float2(0.50, 0.94), float2(0.31, t)));
                on = max(on, ((m /   2) % 2) * bar(p, float2(0.86, 0.72), float2(t, 0.19)));
                on = max(on, ((m /   4) % 2) * bar(p, float2(0.86, 0.28), float2(t, 0.19)));
                on = max(on, ((m /   8) % 2) * bar(p, float2(0.50, 0.06), float2(0.31, t)));
                on = max(on, ((m /  16) % 2) * bar(p, float2(0.14, 0.28), float2(t, 0.19)));
                on = max(on, ((m /  32) % 2) * bar(p, float2(0.14, 0.72), float2(t, 0.19)));
                on = max(on, ((m /  64) % 2) * bar(p, float2(0.50, 0.50), float2(0.31, t)));
                return on;
            }

            /// A right-aligned integer of `places` digits, in a box `places` wide.
            float number(float2 p, float2 origin, float2 size, int value, int places)
            {
                float on = 0.0;
                float gap = size.x * 0.24;
                for (int k = 0; k < places; k++)
                {
                    // k = 0 is the rightmost digit.
                    float2 cell = origin + float2((places - 1 - k) * (size.x + gap), 0.0);
                    float2 q = (p - cell) / size;
                    int d = value;
                    for (int sh = 0; sh < k; sh++) d /= 10;
                    d = d % 10;
                    // Blank leading zeroes so a two-digit reading does not sit
                    // behind a phantom 0 — an instrument that always shows three
                    // digits reads as a label, not a measurement.
                    float show = (k == 0 || value >= (int)pow(10.0, (float)k)) ? 1.0 : 0.0;
                    on = max(on, show * digit(q, d));
                }
                return on;
            }

            /// The class glyph: dot, box, cross, ring.
            float glyph(float2 d, float s, int cls)
            {
                float r = length(d);
                if (cls == 0) return 1.0 - smoothstep(s * 0.55, s, r);
                if (cls == 1) return bar(d, float2(0, 0), float2(s * 0.72, s * 0.72))
                                   - bar(d, float2(0, 0), float2(s * 0.40, s * 0.40));
                if (cls == 2) return max(bar(d, float2(0, 0), float2(s * 1.05, s * 0.20)),
                                         bar(d, float2(0, 0), float2(s * 0.20, s * 1.05)));
                return saturate(line_at(r, s * 0.85, s * 0.36));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                uv.x = lerp(uv.x, 1.0 - uv.x, step(0.5, _FlipX));

                // Tube curvature, applied before anything is drawn so every
                // element bends with the glass rather than sliding across it.
                float2 c = uv * 2.0 - 1.0;
                c *= 1.0 + _Curve * dot(c, c) * 0.5;
                float2 s = c * 0.5 + 0.5;

                float inside = step(0.0, s.x) * step(s.x, 1.0)
                             * step(0.0, s.y) * step(s.y, 1.0);

                float ink = 0.0;   // accumulated beam intensity

                // ---- frame ------------------------------------------------
                float border = max(max(line_at(s.x, 0.022, 0.004), line_at(s.x, 0.978, 0.004)),
                                   max(line_at(s.y, 0.020, 0.004), line_at(s.y, 0.980, 0.004)));
                ink += border * 0.55;
                ink += line_at(s.y, 0.855, 0.0035) * 0.45;   // under the header
                ink += line_at(s.y, 0.150, 0.0035) * 0.45;   // over the footer

                // ---- header: title blocks and the two readouts -------------
                // Fixed block pattern rather than glyphs: this is a legend, and
                // a legend that needs a font atlas is a legend that does not
                // ship. The rhythm is what makes it read as a word.
                // 0b11011010111 read low bit first — the block rhythm of a
                // word. Held as a literal rather than an array for the same
                // portability reason as `seg_mask`.
                int titleBits = 1751;
                for (int ti = 0; ti < 11; ti++)
                {
                    float2 cellC = float2(0.325 + ti * 0.0295, 0.918);
                    float lit_t = (titleBits >> ti) & 1;
                    ink += lit_t * bar(s, cellC, float2(0.0105, 0.020)) * 0.85;
                }
                // Bracket ends, as on a bezel legend.
                ink += bar(s, float2(0.300, 0.918), float2(0.004, 0.026)) * 0.9;
                ink += bar(s, float2(0.302, 0.895), float2(0.010, 0.004)) * 0.9;
                ink += bar(s, float2(0.302, 0.941), float2(0.010, 0.004)) * 0.9;
                ink += bar(s, float2(0.667, 0.918), float2(0.004, 0.026)) * 0.9;
                ink += bar(s, float2(0.665, 0.895), float2(0.010, 0.004)) * 0.9;
                ink += bar(s, float2(0.665, 0.941), float2(0.010, 0.004)) * 0.9;

                // Left: how many contacts are out there in total. Right: the
                // distance to the nearest one, which is the number a player
                // actually navigates by.
                ink += number(s, float2(0.055, 0.892), float2(0.028, 0.052),
                              (int)round(_Contacts), 2) * 1.0;
                ink += number(s, float2(0.760, 0.892), float2(0.028, 0.052),
                              (int)round(_Nearest), 3) * 1.0;
                // Decimal-ish separator so the right-hand group reads as a
                // measurement rather than a second count.
                ink += bar(s, float2(0.742, 0.900), float2(0.005, 0.006)) * 0.8;

                // ---- radar sector -----------------------------------------
                // Origin low and centred; the display is a 180 degree forward
                // PPI, matching the instrument it is modelled on. Contacts
                // behind the holder cannot live on that arc, so they get the
                // rear strip in the footer instead of being dropped.
                float2 o = float2(0.5, 0.185);
                float2 p = float2((s.x - o.x) * _Aspect, s.y - o.y);
                float R = 0.615;
                float r = length(p) / R;
                float ang = atan2(p.x, max(p.y, 1e-5));      // -PI/2 .. PI/2
                float fwd = step(0.0, p.y);

                float grid = 0.0;
                for (int gi = 1; gi <= 4; gi++)
                    grid = max(grid, line_at(r, gi * 0.25, 0.0045) * (gi == 4 ? 1.0 : 0.45));
                for (int ki = 0; ki < 7; ki++)
                {
                    float a = -PI * 0.5 + ki * (PI / 6.0);
                    grid = max(grid, line_at(ang, a, 0.010) * step(r, 1.0) * 0.30);
                }
                // Dotted range rings: broken arcs read as instrument etching,
                // solid ones read as a logo.
                float dots = step(0.5, frac(ang * 7.0 / PI + 0.5));
                grid *= lerp(0.45, 1.0, dots);
                grid *= step(r, 1.02) * fwd;
                ink += grid * 0.75;

                // Sweep: a beam that crosses left to right and repeats, with a
                // phosphor tail behind it. `_Sweep` is driven by the CPU so the
                // beam and the moment a contact is refreshed are the same event.
                float sweepAng = lerp(-PI * 0.5, PI * 0.5, _Sweep);
                float behind = sweepAng - ang;
                float trail = 0.85;
                float tail = saturate(1.0 - behind / trail) * step(0.0, behind);
                tail = tail * tail * tail;
                float beam = line_at(ang, sweepAng, 0.014);
                ink += (tail * 0.35 + beam * 0.9) * step(r, 1.0) * fwd;

                // ---- contacts ---------------------------------------------
                int count = (int)min(_BlipCount, (float)MAX_BLIPS);
                float rearInk = 0.0;
                for (int bi = 0; bi < MAX_BLIPS; bi++)
                {
                    if (bi >= count) break;
                    float4 blip = _Blips[bi];
                    int cls = (int)blip.w;

                    // Contacts flare as the beam passes them and decay after —
                    // the reason the sweep exists at all.
                    float bang = atan2(blip.x, max(blip.y, 1e-5));
                    float lag = sweepAng - bang;
                    lag += step(lag, -0.001) * PI;            // wrapped last pass
                    float lit = saturate(1.0 - lag / 2.2);
                    lit = 0.35 + 0.65 * lit * lit;

                    if (blip.y >= 0.0)
                    {
                        float2 dv = p - float2(blip.x, blip.y) * R;
                        float mark = glyph(dv, 0.019, cls);
                        // Halo, so a contact is findable before it is legible.
                        float halo = (1.0 - smoothstep(0.0, 0.055, length(dv))) * 0.35;
                        ink += (mark + halo) * lit * blip.z * 1.5;
                    }
                    else
                    {
                        // Rear strip: same lateral position, parked in the
                        // footer, so "behind me and to the left" is still
                        // information the player can act on.
                        float2 dr = float2((s.x - 0.5) * _Aspect - blip.x * R * 0.86,
                                           s.y - 0.082);
                        rearInk += glyph(dr, 0.013, cls) * lit * blip.z;
                    }
                }
                // Weighted up hard relative to the arc. A rear contact is the
                // one the player cannot see and most wants told about, and at
                // the arc's own brightness it read as smudge on the bezel.
                ink += rearInk * 2.4;

                // ---- footer: rear strip rail and range legend -------------
                // Rail plus end caps, so the strip reads as its own register
                // rather than as blips that fell off the bottom of the arc.
                ink += line_at(s.y, 0.082, 0.0022) * step(abs(s.x - 0.5), 0.40) * 0.45;
                for (int ei = 0; ei < 2; ei++)
                {
                    float ex = 0.5 + (ei * 2.0 - 1.0) * 0.40;
                    ink += bar(s, float2(ex, 0.082), float2(0.0028, 0.010)) * 0.7;
                }
                for (int mi = 0; mi < 5; mi++)
                {
                    float x = 0.12 + mi * 0.19;
                    ink += bar(s, float2(x, 0.036), float2(0.0035, 0.012)) * 0.6;
                }
                ink += number(s, float2(0.845, 0.018), float2(0.024, 0.044),
                              (int)round(_RangeM), 3) * 1.0;

                // ---- power state ------------------------------------------
                // Booting or shutting down collapses the picture to a line, the
                // way a tube does. Below full power the instrument is honestly
                // not showing you anything, so nothing is drawn but the line.
                float pw = saturate(_Power);
                float open = smoothstep(0.0, 0.55, pw);
                float band = smoothstep(0.0, 0.5 * open + 0.004, 0.5 - abs(s.y - 0.5));
                ink *= smoothstep(0.35, 0.85, pw);
                ink += (1.0 - smoothstep(0.0, 0.9, pw))
                     * line_at(s.y, 0.5, 0.006 + 0.02 * (1.0 - pw)) * 1.6;
                ink *= band;

                // ---- tube character ---------------------------------------
                float now = _Time.y;
                float scan = 1.0 - _ScanlineDepth * (0.5 + 0.5 * sin(s.y * _ScanlineFreq - now * 8.0));
                float flick = 1.0 - _Flicker * (0.5 + 0.5 * sin(now * 47.0) * hash21(float2(now, 0.5)));
                float snow = (hash21(s * 240.0 + now * 60.0) - 0.5) * _Noise;
                float vig = 1.0 - _Vignette * dot(c, c) * 0.18;

                ink = ink * _Gain * scan * flick * vig + snow * pw;

                fixed3 col = _Deep.rgb * (0.6 + 0.4 * band * pw)
                           + _Phosphor.rgb * saturate(ink);
                // Bloom the hottest returns toward white, as a phosphor does
                // when the beam dwells.
                col += saturate(ink - 1.0) * 0.55;

                return fixed4(col * inside, 1.0);
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}
