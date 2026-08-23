// The halo around an aperture — the part that spills onto the wall.
//
// Separate from PortalSurface because the two have opposite needs. The surface
// is opaque and writes depth, so that it can be a hole in the world; the halo
// is additive, writes no depth, and must be allowed to sit slightly in front of
// the wall without ever occluding anything. Trying to do both in one pass means
// choosing which of those to break.
//
// Drawn on a quad larger than the aperture, so the ring lands outside the hole.
Shader "SpaceGame/Portal/PortalRim"
{
    Properties
    {
        _Colour     ("Colour", Color) = (1.00, 0.80, 0.20, 1)
        _HotColour  ("Hot colour", Color) = (1.00, 0.96, 0.72, 1)
        _Intensity  ("Intensity", Range(0.0, 12.0)) = 3.2

        _Radius     ("Ring radius", Range(0.1, 1.0)) = 0.62
        _Thickness  ("Ring thickness", Range(0.01, 0.6)) = 0.10
        _Falloff    ("Outer falloff", Range(0.5, 8.0)) = 2.6

        _Sparks     ("Spark density", Range(0.0, 40.0)) = 14.0
        _SparkSpeed ("Spark speed", Range(0.0, 8.0)) = 2.2
        _Churn      ("Churn", Range(0.0, 1.0)) = 0.45

        _Open       ("Open", Range(0.0, 1.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }

        Pass
        {
            Name "PortalRim"
            Blend One One          // additive: a light source, not a surface
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PortalNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Colour;
                float4 _HotColour;
                float  _Intensity;
                float  _Radius;
                float  _Thickness;
                float  _Falloff;
                float  _Sparks;
                float  _SparkSpeed;
                float  _Churn;
                float  _Open;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 p = IN.uv * 2.0 - 1.0;
                float  r = length(p);
                float  angle = atan2(p.y, p.x);
                float  t = _Time.y;

                float open = saturate(_Open);
                float ringR = _Radius * lerp(0.35, 1.0, open);

                // Ring, wobbled around its circumference so it never reads as
                // a perfect circle drawn on the wall.
                float wobble = (PortalFbm(float2(angle * 3.0, t * 0.4), 3) - 0.5)
                             * _Thickness * _Churn * 2.0;
                float d = abs(r - (ringR + wobble));

                float ring = saturate(1.0 - d / max(_Thickness, 1e-4));
                ring = pow(ring, _Falloff);

                // Sparks orbiting the rim. One-dimensional noise in the angle,
                // scrolled — cheap, and it reads as material being dragged
                // around the aperture rather than as twinkling.
                float orbit = frac(angle / (2.0 * PI) + t * _SparkSpeed * 0.05);
                float spark = PortalValueNoise(float2(orbit * _Sparks, t * 1.7));
                spark = pow(saturate(spark), 8.0);

                float radial = saturate(1.0 - abs(r - ringR) / (_Thickness * 2.2));
                float glow = ring + spark * radial * 2.0;

                // Fade the whole halo as the aperture closes, and kill anything
                // outside the quad's inscribed circle so the corners stay clean.
                glow *= open * saturate(1.0 - smoothstep(0.94, 1.0, r));

                // Hot core, warm falloff: a halo that is one flat colour reads
                // as a decal. The brightest part of a real discharge is nearly
                // white and only the fringe carries the hue.
                float3 tint = lerp(_Colour.rgb, _HotColour.rgb, saturate(glow * 0.8));

                return half4(tint * glow * _Intensity, glow);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
