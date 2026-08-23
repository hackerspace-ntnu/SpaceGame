Shader "SpaceGame/LaserSmoke"
{
    // The wisp of smoke coming off what the Laser Staff is burning through.
    //
    // Alpha-blended, not additive — this is the one part of the impact that has to DARKEN the
    // scene. Everything else there is light being added, and a smoke plume drawn additively over
    // a bright desert reads as pale steam rising off nothing.
    //
    // The puff is a soft disc eroded by scrolling noise. The erosion is what stops it looking like
    // an airbrushed circle: without it, a dozen overlapping soft discs average into one smooth
    // blob no matter how they are animated, because averaging is exactly what soft discs do.
    Properties
    {
        _Softness  ("Edge Softness",      Range(0.05, 1)) = 0.55
        _NoiseScale("Noise Scale",        Range(0.5, 12)) = 3.5
        _Erosion   ("Erosion",            Range(0, 1)) = 0.55
        _Drift     ("Noise Drift Speed",  Range(0, 4)) = 0.35
        _Opacity   ("Opacity",            Range(0, 2)) = 0.75
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Blend SrcAlpha OneMinusSrcAlpha
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
                float _Softness;
                float _NoiseScale;
                float _Erosion;
                float _Drift;
                float _Opacity;
            CBUFFER_END

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float noise21(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float sum = 0.0, amp = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    sum += noise21(p) * amp;
                    p *= 2.03;
                    amp *= 0.5;
                }
                return sum;
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
                float2 p = IN.uv * 2.0 - 1.0;
                float  r = length(p);
                if (r > 1.0) discard;

                float disc = smoothstep(1.0, 1.0 - _Softness, r);

                float n = fbm(IN.uv * _NoiseScale + _Time.y * _Drift);

                // Erode rather than multiply. Multiplying by noise dims the whole puff evenly and
                // it just looks fainter; subtracting eats HOLES in it, which is what gives a plume
                // its ragged silhouette.
                float alpha = saturate(disc - (1.0 - n) * _Erosion);

                alpha *= IN.color.a * _Opacity;

                return half4(IN.color.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
