// Paint hitting a surface.
//
// This is the one place in the effect where the expensive, correct thing is affordable, so this is
// where it is spent. A splat is a SINGLE quad, so it can evaluate a real smooth-union field over a
// handful of lobes and get the merged, metaball outline that a pile of separate droplets can only
// approximate: one fat centre, a ring of satellites at varying distances, and the whole thing
// welded together where they overlap. The droplets in flight (PortalGoo.shader) buy their thickness
// from lighting instead, because there are hundreds of them and this field costs a loop.
//
// It runs on its own clock, from _Born, and does three things over its life:
//
//   • IMPACT. The lobes fly outward from the centre over the first fraction of a second, so the
//     splat spreads rather than appearing at full size. A splat that pops in at its final shape
//     reads as a decal being switched on.
//   • DRIP. Gravity is projected into the quad's own plane by the C# that spawns it, so the same
//     shader drips DOWN a wall and pools flat on a floor without knowing which it is on.
//   • DRY. It thins and fades from the edges in, so the wall is not left permanently painted.
//
// Alpha-blended and depth-testing but not depth-writing: it is a coat of paint lying on geometry,
// not geometry. Slight polygon offset keeps it off the surface it is painted on.
Shader "SpaceGame/Portal/PortalSplat"
{
    Properties
    {
        _Colour     ("Colour", Color) = (1.00, 0.76, 0.10, 1)
        _HotColour  ("Sheen colour", Color) = (1.00, 0.96, 0.72, 1)

        _Lobes      ("Lobe count", Range(3, 12)) = 7
        _Spread     ("Lobe spread", Range(0.0, 1.0)) = 0.52
        _LobeSize   ("Lobe size", Range(0.05, 0.6)) = 0.24
        _CoreSize   ("Core size", Range(0.05, 0.8)) = 0.34
        _Smooth     ("Weld", Range(0.01, 0.5)) = 0.13
        _Seed       ("Seed", Float) = 0.0

        _Drip       ("Drip length", Range(0.0, 2.0)) = 0.55
        _Gravity    ("Gravity in quad space", Vector) = (0, -1, 0, 0)

        _Born       ("Spawn time", Float) = 0.0
        _Spread01   ("Spread duration", Range(0.02, 1.0)) = 0.18
        _Life       ("Lifetime", Range(0.5, 30.0)) = 8.0
        _Fade       ("Fade duration", Range(0.1, 10.0)) = 2.5

        _Sheen      ("Sheen strength", Range(0.0, 3.0)) = 1.1
        _Edge       ("Edge softness", Range(0.001, 0.2)) = 0.02
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
            Name "PortalSplat"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PortalNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Colour;
                float4 _HotColour;
                float  _Lobes;
                float  _Spread;
                float  _LobeSize;
                float  _CoreSize;
                float  _Smooth;
                float  _Seed;
                float  _Drip;
                float4 _Gravity;
                float  _Born;
                float  _Spread01;
                float  _Life;
                float  _Fade;
                float  _Sheen;
                float  _Edge;
            CBUFFER_END

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

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
                OUT.uv = IN.uv;
                return OUT;
            }

            float SmoothMin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            // A circle stretched into a teardrop along gravity, which is what a blob of paint on a
            // vertical surface actually is. The stretch is one-sided: the lobe keeps its shape on
            // the up-slope side and runs on the down-slope one.
            float Teardrop(float2 q, float2 centre, float radius, float2 down, float run)
            {
                float2 delta = q - centre;

                // How far along gravity this fragment is from the lobe, clamped to the run — the
                // coordinate is squashed inside that band, which drags the circle into a tail.
                float along = clamp(dot(delta, down), 0.0, run);
                delta -= down * along;

                // The tail narrows as it runs, or a drip reads as a rectangle.
                float taper = radius * (1.0 - 0.65 * saturate(along / max(run, 1e-4)));

                return length(delta) - taper;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 q = IN.uv * 2.0 - 1.0;

                float age = max(_Time.y - _Born, 0.0);
                float spread = saturate(age / _Spread01);

                // Ease-out: paint leaves the impact fast and then stops, rather than sliding out at
                // a constant rate, which reads as the decal being scaled up.
                spread = 1.0 - (1.0 - spread) * (1.0 - spread);

                float2 down = normalize(_Gravity.xy + float2(0.0, -1e-5));
                float run = _Drip * spread * saturate(age / max(_Life, 1e-4) * 3.0);

                // The core, always present, sitting at the impact itself.
                float field = Teardrop(q, float2(0.0, 0.0), _CoreSize * spread, down, run);

                int lobes = (int)min(_Lobes, 12.0);
                for (int i = 0; i < lobes; i++)
                {
                    // Deterministic per-lobe placement from the seed. Every machine that spawns this
                    // splat with the same seed draws the same splat.
                    float fi = (float)i;
                    float a = PortalValueNoise(float2(fi * 3.7 + _Seed, 11.3)) * 6.2831853;
                    float dist = 0.35 + 0.65 * PortalValueNoise(float2(fi * 1.9 + _Seed, 27.1));
                    float size = 0.35 + 0.65 * PortalValueNoise(float2(fi * 5.1 + _Seed, 43.7));

                    float2 centre = float2(cos(a), sin(a)) * dist * _Spread * spread;

                    // Satellites run further than the core: the thin outliers are the ones gravity
                    // actually takes down the wall.
                    float lobe = Teardrop(q, centre, _LobeSize * size * spread, down,
                                          run * (0.6 + 0.8 * size));

                    field = SmoothMin(field, lobe, _Smooth);
                }

                // Drying eats the splat from its edges inward, so it thins and breaks up rather
                // than dimming uniformly.
                float dying = saturate((age - (_Life - _Fade)) / max(_Fade, 1e-4));
                field += dying * (_CoreSize + _LobeSize);

                // A little noise on the boundary so no lobe is ever a clean circle.
                field += (PortalFbm(q * 6.0 + _Seed, 3) - 0.5) * 0.045;

                float alpha = 1.0 - smoothstep(-_Edge, _Edge, field);
                clip(alpha - 0.01);

                // Wet sheen: brightest just inside the boundary, where a fresh coat pools thickest
                // and catches the light.
                float sheen = saturate(1.0 - abs(field + 0.05) / 0.09) * _Sheen;

                float3 col = _Colour.rgb;
                col = lerp(col, _HotColour.rgb, saturate(sheen));

                // Deeper towards the middle of the mass, the way a real puddle is darkest where it
                // is deepest.
                col *= lerp(0.72, 1.0, saturate(1.0 + field * 3.0));

                return half4(col, alpha * _Colour.a * (1.0 - dying * 0.35));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
