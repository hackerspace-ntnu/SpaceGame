// The smoke trail off the jetpack's nozzles.
//
// A particle shader with NO TEXTURE. The puff is drawn from the quad's own UVs — a soft disc with
// a bitten edge — because this project builds its look in code and a soft-smoke texture would be
// the only art asset in the whole item. It also means the softness is a number somebody can tune
// rather than a file somebody has to repaint.
//
// It is ALPHA BLENDED, unlike JetFlame, and that is the whole difference between the two: fire adds
// light and smoke takes it away. An additive plume would glow white against the sand and read as
// steam lit from inside.
//
// Per-particle colour comes through the vertex stream, so the ParticleSystem's own colour-over-
// lifetime curve is what fades a puff out. Nothing here needs to know about lifetime.
Shader "SpaceGame/Effects/JetSmoke"
{
    Properties
    {
        _Color     ("Tint",  Color) = (0.34, 0.33, 0.32, 1)
        _Softness  ("Edge Softness", Range(0.01, 1)) = 0.75
        _Density   ("Density",       Range(0, 2))    = 0.7

        // The bite out of the edge. Zero is a clean disc, which reads as a bubble; a little of this
        // is what makes it read as smoke rather than as a sphere.
        _NoiseScale ("Noise Scale", Range(1, 20)) = 5
        _NoiseBite  ("Noise Bite",  Range(0, 1))  = 0.35
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
            Name "JetSmoke"

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
                float4 _Color;
                float _Softness;
                float _Density;
                float _NoiseScale;
                float _NoiseBite;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

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
                OUT.uv = IN.uv;

                // The particle system's own colour and alpha ride the vertex stream, which is how
                // the fade-in and fade-out curves reach the pixel without this shader knowing what
                // a lifetime is.
                OUT.color = IN.color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 centred = IN.uv - 0.5;
                float radius = length(centred) * 2.0;

                // A soft disc. smoothstep rather than a texture lookup: the falloff IS the look,
                // and here it is one tunable number instead of a painted gradient.
                float disc = 1.0 - smoothstep(1.0 - _Softness, 1.0, radius);

                // Bitten at the edge so the silhouette is not a perfect circle. Sampled in the
                // quad's own UV, so each puff's bite is fixed rather than crawling as it drifts.
                float n = Noise(IN.uv * _NoiseScale);
                disc *= 1.0 - _NoiseBite * (1.0 - n);

                float alpha = saturate(disc * _Density) * IN.color.a;
                if (alpha <= 0.003) return 0;

                return half4(_Color.rgb * IN.color.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
