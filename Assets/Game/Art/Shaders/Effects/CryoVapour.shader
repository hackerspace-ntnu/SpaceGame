// The stylized cold every freeze in this project is drawn out of: the sprayer's plume, the frost
// blooming where it lands, and the vapour blowing off a surface that will not take ice.
//
// It is FlameBillboard's opposite number and is built the same way on purpose — a particle shader,
// world-space noise, hard quantized bands — because the two are seen side by side in the same
// hotbar and a soft photoreal mist beside a quantized flame reads as a rendering bug rather than as
// a second element (Environment.md: everything ships through a pastel quantize pass).
//
// WHAT IS DIFFERENT FROM FIRE, AND WHY. Fire rises, so its field scrolls down through the
// particles; cold FALLS, so this one scrolls up through them and the plume sags rather than licks.
// Fire is additive because it only ever adds light; frost is not — it is matter in the air, so this
// blends normally and can actually hide what is behind it, which is what makes a plume held on a
// creature read as burying them rather than as glowing on them.
//
// THE SHARDS ARE THE WHOLE POINT. A cold gas alone is a grey puff. What says "ice" is the crystal:
// the noise is folded into hard facet steps (_Facets), so the silhouette breaks into angular plates
// instead of round lumps, and a sparse high-frequency layer (_Sparkle) throws individual crystals
// catching the light. Both are cut from the same field as the body, so they sit ON the shape rather
// than being stamped over it.
//
// THE COLD IS THE PARTICLE'S OWN COLOUR, NOT ITS AGE. Reading age needs a custom vertex stream, and
// a stream list that drifts out of step with the shader's inputs fails silently — the plume simply
// goes black. The particle system already hands every quad a colour over its lifetime, so the
// system's own gradient decides how cold each particle is and this shader only decides what a given
// coldness looks like. Retuning the plume is then a gradient in the builder, not a shader edit.
Shader "SpaceGame/Effects/CryoVapour"
{
    Properties
    {
        _CoreColor   ("Core Colour",   Color) = (0.98, 1.00, 1.00, 1)
        _IceColor    ("Ice Colour",    Color) = (0.72, 0.93, 1.00, 1)
        _DeepColor   ("Deep Colour",   Color) = (0.36, 0.68, 0.96, 1)
        _ShadowColor ("Shadow Colour", Color) = (0.16, 0.31, 0.62, 1)

        _Brightness ("Brightness", Range(0, 12)) = 2.2

        _CoreEnd ("Core Above", Range(0, 1)) = 0.74
        _IceEnd  ("Ice Above",  Range(0, 1)) = 0.48
        _DeepEnd ("Deep Above", Range(0, 1)) = 0.22

        _NoiseScale ("Noise Scale",   Range(0.1, 20)) = 2.8
        _NoiseSpeed ("Noise Fall",    Range(0, 8))    = 1.1
        _Stretch    ("Vertical Draw", Range(0.05, 2)) = 0.75
        _Curl       ("Curl",          Range(0, 4))    = 1.4
        _Warp       ("Noise Bite",    Range(0, 2))    = 1.0
        _Detail     ("Detail Bite",   Range(0, 1))    = 0.55
        _Fill       ("Body Fill",     Range(1, 6))    = 1.9

        _Facets  ("Facet Steps", Range(1, 12)) = 5
        _Shard   ("Facet Bite",  Range(0, 1))  = 0.45
        _Sparkle ("Sparkle",     Range(0, 4))  = 1.6
        _Rim     ("Rim Light",   Range(0, 2))  = 0.8

        _Cut      ("Cut",       Range(0, 1))    = 0.32
        _Softness ("Cut Width", Range(0.01, 1)) = 0.09
        _Halo     ("Halo",      Range(0, 1))    = 0.14
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Pass
        {
            Name "CryoVapour"

            // Ordinary transparency, not the flame's additive blend: frost is matter and may hide
            // what is behind it. The bright core still reads as light because the colour is scaled
            // well past one before it is written.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _IceColor;
                float4 _DeepColor;
                float4 _ShadowColor;
                float _Brightness;
                float _CoreEnd;
                float _IceEnd;
                float _DeepEnd;
                float _NoiseScale;
                float _NoiseSpeed;
                float _Stretch;
                float _Curl;
                float _Warp;
                float _Detail;
                float _Fill;
                float _Facets;
                float _Shard;
                float _Sparkle;
                float _Rim;
                float _Cut;
                float _Softness;
                float _Halo;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 colour     : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 colour     : COLOR;
            };

            float Hash(float3 p)
            {
                return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453);
            }

            // Trilinear value noise, arithmetic rather than a texture fetch for the reason
            // FlameBillboard gives: this pass is fill-bound, so the budget is spent on ALU rather
            // than on a sampler's memory dependency (GDC-L1-PERF-0004).
            float Noise(float3 p)
            {
                float3 cell = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash(cell);
                float n100 = Hash(cell + float3(1, 0, 0));
                float n010 = Hash(cell + float3(0, 1, 0));
                float n110 = Hash(cell + float3(1, 1, 0));
                float n001 = Hash(cell + float3(0, 0, 1));
                float n101 = Hash(cell + float3(1, 0, 1));
                float n011 = Hash(cell + float3(0, 1, 1));
                float n111 = Hash(cell + float3(1, 1, 1));

                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);

                return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.uv = IN.uv;
                OUT.colour = IN.colour;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // A plateau rather than a cone, for FlameBillboard's reason: a plain radial falloff
                // shades every quad like a little sphere, and a stack of those reads as bubbles
                // whatever is done to the silhouette. `_Fill` saturates the middle and hands the
                // interior to the noise; the circle only keeps the shape clear of the quad's edge.
                float2 centred = IN.uv * 2.0 - 1.0;
                float radius = length(centred);
                float blob = saturate((1.0 - radius) * _Fill);

                // World space: neighbouring particles share the field, so a burst of them condenses
                // as one cloud rather than as a stack of identical puffs.
                float3 samplePoint = IN.positionWS * _NoiseScale;

                // Drawn out vertically far less than fire is. A flame is four times taller than it
                // is wide; cold spills and settles, so the features here are close to round and the
                // field scrolls UP through the particles, which reads as the whole plume sinking.
                samplePoint.y *= _Stretch;
                samplePoint.y += _Time.y * _NoiseSpeed;

                float coarse = Noise(samplePoint);
                float fine = Noise(samplePoint * 2.9 + coarse * _Curl + 7.7);

                // Centred on zero so the noise pushes the silhouette out as often as it eats in. A
                // one-sided subtraction takes two thirds of the body away everywhere at once, which
                // is what a plume of a few dim specks looks like.
                float bite = (coarse - 0.5) * _Warp + (fine - 0.5) * _Detail;

                // THE CRYSTAL. The field is folded into hard steps before it is used, so the edge
                // breaks along flat facets instead of curving. Fire wants none of this — flame has
                // no faces — and it is the single thing that makes this read as ice rather than as
                // smoke that happens to be blue.
                float faceted = floor(bite * _Facets) / _Facets;
                bite = lerp(bite, faceted, _Shard);

                float shape = blob + bite;

                float life = IN.colour.a;

                // The cut rises as the particle dies, so an old puff is eaten into tatters and gone
                // rather than fading as a whole disc.
                float cut = _Cut + (1.0 - life) * 0.3;

                float alpha = smoothstep(cut, cut + _Softness, shape);

                // The one soft thing here: cold air scatters a little light around itself. It
                // follows the same bite as the body, because a halo that ignored the noise would
                // draw a clean round glow on every quad and give the sprite grid away.
                float halo = _Halo * saturate(blob) * life * saturate(0.3 + bite * 2.0);
                alpha = saturate(alpha + halo * 0.5);

                if (alpha <= 0.004) return 0;

                // Four hard bands read off the NOISE rather than off the distance to the centre:
                // banding a radial gradient draws concentric rings, which is how a sphere is
                // shaded, while banding the noise draws plates.
                float chill = saturate((0.55 + bite * 1.15) * life);

                float3 colour = chill > _CoreEnd ? _CoreColor.rgb
                              : chill > _IceEnd  ? _IceColor.rgb
                              : chill > _DeepEnd ? _DeepColor.rgb
                                                 : _ShadowColor.rgb;

                // Individual crystals catching the light: a sparse, much finer field thresholded
                // hard, so what survives is a handful of white specks rather than a dusting over
                // everything. Multiplied by the body's own alpha so nothing sparkles in mid-air
                // where the plume has already been cut away.
                float glint = Noise(samplePoint * 11.0 + float3(0.0, _Time.y * 0.6, 0.0));
                float spark = smoothstep(0.86, 0.97, glint) * _Sparkle * life;
                colour += _CoreColor.rgb * spark;

                // A cold rim: the edge of a frost cloud is where it is thinnest and brightest, and
                // it is what keeps a pale plume legible against pale sand.
                colour += _IceColor.rgb * _Rim * saturate(radius * radius - 0.25) * life;

                // The system's start colour tints the bands, which is how one material serves the
                // white plume at the muzzle and the deeper blue of frost forming on a body.
                colour *= IN.colour.rgb;

                return half4(colour * _Brightness, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
