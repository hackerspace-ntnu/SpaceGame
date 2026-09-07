// The four flames out of the jetpack's nozzles.
//
// Stylized on purpose and not a fire simulation: hard colour bands, a threshold-cut noise that
// breaks the tip into blobs, and a soft halo around the whole thing. It sits with the shipped
// pastel-quantize look (Environment.md) rather than fighting it — a soft photoreal plume next to
// that palette reads as a bug rather than as fire.
//
// THE MESH IS A UNIT CONE, AND THAT IS WHY THIS SHADER HAS NO MEASUREMENTS IN IT. JetpackBuilder
// generates the flame geometry itself: base at y = 0 with radius 1, tip at y = 1 with radius 0, so
// object space IS the plume's own frame and every number below is a fraction. The cone's transform
// carries the real size.
//
// That convention was arrived at the hard way. The first cut treated the model's own
// `Mesh_Jetpack_Exhaust*` geometry as the plume and measured its longest axis for a flow direction
// — the trick the wingsuit's spar taught. It does not survive contact with this model: those
// meshes are 5.9 x 2.6 x 5.9 cm DISCS, so the longest axis is a tie between two axes across the
// disc and the real plume direction is the SHORTEST one, the disc's normal. Generating the cone
// removes the guess rather than making it more cleverly.
//
// THE THROTTLE IS SPENT ON THE TRANSFORM, NOT HERE. JetpackNozzles scales the cone's Y by the
// throttle, so a hover is already a short cone in object space. This shader used to ALSO divide
// its length coordinate by the throttle, which discarded the far end of a cone that had already
// been shortened — a hover drew a stub of a stub. The throttle still reaches the pixel, but only
// as brightness and as how far up the noise is allowed to eat.
Shader "SpaceGame/Effects/JetFlame"
{
    Properties
    {
        _CoreColor ("Core Colour", Color) = (1.00, 0.98, 0.90, 1)
        _MidColor  ("Mid Colour",  Color) = (1.00, 0.72, 0.20, 1)
        _EdgeColor ("Edge Colour", Color) = (0.93, 0.28, 0.06, 1)
        _TipColor  ("Tip Colour",  Color) = (0.60, 0.10, 0.03, 1)
        _HotColor  ("Overheat Tint", Color) = (0.95, 0.10, 0.03, 1)

        _Throttle  ("Throttle",   Range(0, 1)) = 0
        _Heat      ("Heat",       Range(0, 1)) = 0
        _Brightness("Brightness", Range(0, 8)) = 3.2

        _CoreEnd ("Core Ends At", Range(0.01, 1)) = 0.18
        _MidEnd  ("Mid Ends At",  Range(0.01, 1)) = 0.46
        _EdgeEnd ("Edge Ends At", Range(0.01, 1)) = 0.74
        _Taper   ("Taper",        Range(0.1, 4))  = 1.5

        _Halo      ("Halo",       Range(0, 1)) = 0.35
        _HaloWidth ("Halo Width", Range(0.01, 2)) = 0.6

        _FlickerScale ("Flicker Scale", Range(1, 40)) = 9
        _FlickerSpeed ("Flicker Speed", Range(0, 40)) = 18
        _FlickerCut   ("Flicker Cut",   Range(0, 0.95)) = 0.5
        _Swirl        ("Swirl",         Range(0, 8))    = 2.2
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
            Name "JetFlame"

            Blend SrcAlpha One   // additive: fire adds light, it never takes any away
            ZWrite Off
            ZTest LEqual
            Cull Off             // a thin cone seen from any side is the same flame, and the
                                 // mirrored left-hand pod would otherwise cull inside out

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _MidColor;
                float4 _EdgeColor;
                float4 _TipColor;
                float4 _HotColor;
                float _Throttle;
                float _Heat;
                float _Brightness;
                float _CoreEnd;
                float _MidEnd;
                float _EdgeEnd;
                float _Taper;
                float _Halo;
                float _HaloWidth;
                float _FlickerScale;
                float _FlickerSpeed;
                float _FlickerCut;
                float _Swirl;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;

                // x: how far along the plume, 0 at the nozzle and 1 at the tip.
                // y: how far off its axis, 0 on the centre line and 1 at the base radius.
                // z: angle around the plume, 0..1. What the swirl runs along.
                float3 flow : TEXCOORD0;
            };

            // Cheap value noise. Deliberately not a texture: the flame is four small cones and a
            // sampler here would be a fetch and a memory dependency for something two arithmetic
            // instructions can do badly enough to look right.
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash(cell);
                float b = Hash(cell + float2(1, 0));
                float c = Hash(cell + float2(0, 1));
                float d = Hash(cell + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);

                // Object space, not world: the cone's frame is the nozzle's own, so the bands stay
                // pinned to the flame however the pod gimbals and however the wearer turns. In
                // world space they would slide along the plume every time the player looked left.
                float2 across = IN.positionOS.xz;

                OUT.flow = float3(saturate(IN.positionOS.y),
                                  saturate(length(across)),
                                  atan2(across.y, across.x) * 0.1591549);   // /(2 pi)
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                if (_Throttle <= 0.001) return 0;

                float t = IN.flow.x;

                // Four hard bands. Quantized rather than blended, which is the whole look: a
                // smooth ramp here is a photoreal plume, and next to the quantized palette that is
                // the thing that looks broken. The fourth band is what gives the tip somewhere to
                // go dark before it breaks up.
                float3 colour = t < _CoreEnd ? _CoreColor.rgb
                              : t < _MidEnd  ? _MidColor.rgb
                              : t < _EdgeEnd ? _EdgeColor.rgb
                                             : _TipColor.rgb;

                // Overheating dirties the whole flame toward red. The tips already glow through
                // their own material; this is the fire agreeing with them.
                colour = lerp(colour, _HotColor.rgb, _Heat * 0.55);

                // The cone already narrows; this decides how much of its width is actually alight,
                // which is what keeps the edge from reading as a hard-shaded solid.
                float width = pow(saturate(1.0 - t), 1.0 / _Taper);
                float radial = IN.flow.y / max(width, 1e-3);
                float body = saturate(1.0 - radial);

                // Scrolled DOWN the plume and around it, and CUT rather than multiplied, so the
                // far half breaks into separate blobs instead of shimmering. The cut deepens
                // toward the tip, which is why the root stays a solid cone and only the end
                // flickers — and it deepens further at low throttle, so a hover sputters where
                // full thrust is a clean lance.
                float scroll = _Time.y * _FlickerSpeed;
                float2 noiseUv = float2(IN.flow.z * _FlickerScale + t * _Swirl,
                                        t * _FlickerScale - scroll);
                float n = Noise(noiseUv);

                float cut = _FlickerCut * t * lerp(1.6, 1.0, _Throttle);
                float flicker = smoothstep(cut, cut + 0.2, n);

                float core = body * lerp(1.0, flicker, t);

                // A soft halo outside the body, so the flame throws some light into the air around
                // it rather than ending at a hard silhouette. It is deliberately NOT flickered:
                // the blobs are the shape, the halo is the glow, and flickering both makes the
                // whole plume strobe.
                float halo = _Halo * saturate(1.0 - (radial - 1.0) / _HaloWidth)
                                   * saturate(1.0 - t);

                float alpha = saturate(core + halo) * _Throttle;
                if (alpha <= 0.003) return 0;

                return half4(colour * _Brightness * alpha, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
