// The stylized flame every fire in this project is drawn out of: the flamethrower's jet, the
// billows rolling off its tip, the embers it throws, and the patches it leaves burning on the sand.
//
// A PARTICLE shader, not a mesh one. JetFlame.shader next door draws ONE cone per nozzle and can
// therefore measure everything against that cone's own object space. A jet that has to reach six
// metres, keep burning after the trigger comes up, and pool on the ground where it lands is not one
// cone — it is hundreds of quads, each with its own life. So the shape here comes from the quad's
// own UV and the volume comes from noise sampled in WORLD space, which is the whole trick: adjacent
// particles sample the same field, so a cluster of quads reads as one boiling mass instead of as a
// stack of identical puffs. Sample it in UV space instead and every particle wears the same stamp,
// which is exactly what "I can see the sprites" looks like.
//
// THE QUAD IS A FLAME, NOT A PUFF. The shape is a candle profile in the quad's OWN uv — rounded at
// the root, bellied, drawn to a point at the tip — and only then eaten by the noise. A radial
// falloff cannot be made to read as fire however hard it is chewed: it is round, so it is a bubble,
// and every trick that hides that (plateaus, banding off the noise) only hides it. A flame has an
// AXIS, and the axis is what the eye reads. The particle system holds up its end by rotating the
// quads barely at all, so every tongue points up the screen; a full 0..2pi start rotation is what
// forces a sprite to be round in the first place.
//
// STYLIZED MEANS QUANTIZED. Four hard colour bands and a hard alpha cut, no soft falloff anywhere
// except a thin halo. That is the same decision JetFlame made and for the same reason: the shipped
// look is a pastel quantize pass (Environment.md), and a soft photoreal plume in front of it reads
// as a rendering bug rather than as fire.
//
// THE HEAT IS THE PARTICLE'S OWN COLOUR, NOT ITS AGE. Reading age needs a custom vertex stream, and
// a stream list that drifts out of step with the shader's inputs fails silently — the flame simply
// goes black. The particle system already hands every quad a colour over its lifetime, so the
// system's own gradient decides how hot each particle is and this shader only decides what a given
// heat looks like. Retuning the fire is then a gradient in the builder, not a shader edit.
Shader "SpaceGame/Effects/FlameBillboard"
{
    Properties
    {
        _CoreColor ("Core Colour", Color) = (1.00, 0.97, 0.85, 1)
        _MidColor  ("Mid Colour",  Color) = (1.00, 0.76, 0.22, 1)
        _EdgeColor ("Edge Colour", Color) = (0.95, 0.35, 0.05, 1)
        _SootColor ("Soot Colour", Color) = (0.42, 0.11, 0.03, 1)

        _Brightness ("Brightness", Range(0, 12)) = 3.4

        _CoreEnd ("Core Above", Range(0, 1)) = 0.72
        _MidEnd  ("Mid Above",  Range(0, 1)) = 0.46
        _EdgeEnd ("Edge Above", Range(0, 1)) = 0.20

        _NoiseScale ("Noise Scale",   Range(0.1, 30)) = 14
        _NoiseSpeed ("Noise Rise",    Range(0, 8))    = 1.6
        _Stretch    ("Vertical Draw", Range(0.05, 2)) = 0.35
        _Curl       ("Curl",          Range(0, 4))    = 1.9
        _Warp       ("Noise Bite",    Range(0, 2))    = 1.15
        _Detail     ("Detail Bite",   Range(0, 2))    = 0.9
        _Fill       ("Edge Hardness", Range(1, 6))    = 2.6

        _Belly   ("Flame Width",   Range(0.1, 1))  = 0.72
        _Root    ("Root Round",    Range(0.02, 1)) = 0.55
        _Tip     ("Tip Draw",      Range(0.2, 3))  = 0.9
        _Sway    ("Tongue Sway",   Range(0, 2))    = 0.45
        _TipBite ("Tip Shredding", Range(0, 4))    = 2.4
        _Cool    ("Tip Cooling",   Range(0, 1.5))  = 0.55
        _Wither  ("Death Width",   Range(0, 1))    = 0.8

        _Cut      ("Cut",       Range(0, 1))    = 0.3
        _Softness ("Cut Width", Range(0.01, 1)) = 0.07
        _Halo     ("Halo",      Range(0, 1))    = 0.1
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
            Name "FlameBillboard"

            Blend SrcAlpha One   // additive: fire adds light and never takes any away
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
                float4 _MidColor;
                float4 _EdgeColor;
                float4 _SootColor;
                float _Brightness;
                float _CoreEnd;
                float _MidEnd;
                float _EdgeEnd;
                float _NoiseScale;
                float _NoiseSpeed;
                float _Stretch;
                float _Curl;
                float _Warp;
                float _Detail;
                float _Fill;
                float _Belly;
                float _Root;
                float _Tip;
                float _Sway;
                float _TipBite;
                float _Cool;
                float _Wither;
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

            // Trilinear value noise. Deliberately arithmetic rather than a texture fetch: the fire
            // is fill-heavy by nature — hundreds of overlapping transparent quads — and on a
            // fill-bound pass a sampler is a memory dependency where this is a handful of ALU
            // (GDC-L1-PERF-0004: the budget here is fill rate, so spend on maths, not on bandwidth).
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
                // The quad's own axis. `height` runs 0 at the root of the flame to 1 at its tip,
                // `across` is signed either side of that axis. Everything below is measured against
                // those two, which is the difference between a flame and a puff: a puff has a
                // centre and a radius, a flame has a direction.
                float height = IN.uv.y;
                float across = IN.uv.x * 2.0 - 1.0;

                // The particle system's own colour-over-lifetime is the heat: white-hot at birth,
                // dying to soot. Its alpha is how much of the particle is left to burn.
                float life = IN.colour.a;

                // World space, and rising. Two things follow from that: neighbouring particles
                // share the field, so a clump of them boils as one body; and the field scrolls
                // DOWNWARD through them, so every flame licks upward regardless of which way the
                // particle itself is drifting.
                float3 samplePoint = IN.positionWS * _NoiseScale;

                // Value noise is isotropic, and round cells eat round holes. Compressing the
                // sample's Y draws every feature several times taller than it is wide, so the
                // noise erodes the flame in streaks along its axis rather than in scallops.
                samplePoint.y *= _Stretch;
                samplePoint.y -= _Time.y * _NoiseSpeed;

                float coarse = Noise(samplePoint);

                // The coarse octave DISPLACES the second one rather than only adding to it. A
                // domain warp costs nothing here — the value is already in a register — and it is
                // the difference between noise cells, which are lumps, and filaments, which lick.
                float fine = Noise(samplePoint * 2.7 + coarse * _Curl + 19.3);

                // Centred on zero, so the noise pushes the silhouette OUT as often as it eats into
                // it. The first cut of this shader subtracted (1 - noise) instead, which is only
                // ever a subtraction — average noise took two thirds of the body away everywhere
                // at once and the fire came out as a few dim specks. A bite has to have a tooth
                // and a gap.
                float bite = (coarse - 0.5) * _Warp + (fine - 0.5) * _Detail;

                // The tongue wags off its axis, and only the upper half of it does: a flame is
                // planted at the root and loose at the tip. This is a shear, not a wobble — the
                // whole upper body leans together, so the shape stays one tongue.
                across -= bite * _Sway * height;

                // The candle profile. Rounded at the root so the base of the quad is not a cut-off
                // stump, drawn to a point at the tip by `_Tip`, widest in between. `_Belly` is a
                // half-width in uv, so it also keeps the shape clear of the quad's edge however far
                // the sway and the noise push it.
                float rootRound = saturate(sqrt(saturate(height / _Root)));
                float tipDraw = pow(saturate(1.0 - height), _Tip);
                float width = _Belly * rootRound * tipDraw;

                // The noise BREATHES the width rather than being added to the shape, and the tip
                // breathes hardest — hard enough to pinch the tongue off entirely and leave a lick
                // flying free above it. Added instead, the noise paints wherever it likes: at the
                // tip, where the profile has almost no width left to defend itself, an added bite
                // is the whole signal, so the top of every quad fills back in with a square-edged
                // cloud and the fire is a raft of blobs again.
                width *= saturate(1.0 + bite * lerp(0.3, _TipBite, height));

                // A dying flame is DRAWN THIN, not faded and not cut back. Raising the alpha cut as
                // it died was the obvious way to do this and it is wrong: the cut eats the eroded
                // edge first and leaves the solid middle standing, so every flame ended its life as
                // a smooth lens — the last of the bubbles, and the ones that hang longest in the
                // air where they are easiest to look at.
                width *= lerp(_Wither, 1.0, life);

                // Normalized against the local width, so the edge is equally hard at the tip, where
                // the flame is a few pixels wide, as it is at the belly. Divide by a constant here
                // instead and the tip dissolves into a smudge while the belly stays hard-edged.
                float shape = saturate((width - abs(across)) / max(width, 1e-4) * _Fill);

                float alpha = smoothstep(_Cut, _Cut + _Softness, shape);

                // The one soft thing in here: the fire throws a little light into the air around
                // itself rather than ending on a hard silhouette. It follows the SAME shape and the
                // same bite, though — a halo that ignored them drew a clean round glow on every
                // quad regardless of what shape the flame had been eaten into, which was the single
                // biggest reason the jet read as a raft of bubbles. It is still not cut, so the
                // glow stays smooth where it survives and the jet does not strobe.
                float halo = _Halo * shape * life * saturate(0.3 + bite * 2.0);

                alpha = saturate(alpha + halo * 0.5);
                if (alpha <= 0.004) return 0;

                // Four hard bands, read off the noise AND off the height up the flame. A flame is
                // hottest where it is fed and coolest where it is coming apart, so the tip bands
                // down into soot on its own; banding a radial gradient instead draws concentric
                // rings, which is exactly how a sphere is shaded. The particle's remaining life
                // still cools the whole thing, so a young flame has a white heart and an old one is
                // soot throughout.
                float heat = saturate((0.82 + bite * 0.8 - height * _Cool) * life);

                float3 colour = heat > _CoreEnd ? _CoreColor.rgb
                              : heat > _MidEnd  ? _MidColor.rgb
                              : heat > _EdgeEnd ? _EdgeColor.rgb
                                                : _SootColor.rgb;

                // The system's start colour tints the bands, which is how one material serves the
                // white-hot root of the jet and the deep orange of a patch burning on the sand.
                colour *= IN.colour.rgb;

                return half4(colour * _Brightness * alpha, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
