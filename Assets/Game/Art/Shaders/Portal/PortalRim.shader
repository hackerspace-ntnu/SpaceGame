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

        _Radius     ("Ring offset outside the edge (x depth)", Range(-0.5, 1.0)) = 0.06
        _Thickness  ("Ring thickness (x depth)", Range(0.01, 1.0)) = 0.22
        _Falloff    ("Outer falloff", Range(0.5, 8.0)) = 2.6

        // Must equal PortalSurface's _Crawl on the matching aperture — the two draw one edge.
        // PortalContentBuilder writes both from one constant.
        _Crawl      ("Edge crawl (m)", Range(0.0, 0.4)) = 0.06

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

            // Biased towards the camera. The aperture is a flat plane lying on ground that is not
            // flat, and Portal.ConformToSurface already lifts it clear of the highest bump under
            // the paint — this covers the residual, so a portal sprayed on terrain can never lose
            // the depth test to the hillside it is painted on and vanish in patches.
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PortalNoise.hlsl"
            #include "PortalStencil.hlsl"

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
                float  _Crawl;
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
                float  angle;
                float  d = PortalStencilDistance(IN.uv, angle);
                float  t = _Time.y;

                float open = saturate(_Open);

                // The halo rides the outline in METRES, the same way the surface does, so it stops
                // resizing itself every time a blob of paint widens the aperture somewhere else.
                // _Radius is now how far outside the edge the ring sits, as a share of _Depth.
                // The iris erodes by _CloseDepth so the ring follows the surface all the way shut —
                // see PortalSurface.
                d += (1.0 - open) * max(_CloseDepth, _Depth);

                // The SAME crawling edge the aperture draws. The halo used to roll noise of its
                // own here — a different frequency at a different speed — so the ring was drawn
                // around an outline the hole did not have: the two wandered a tenth of a metre
                // apart and shimmered out of phase. One edge, one definition, both shaders.
                d += PortalStencilCrawl(angle, _Crawl);

                // Churn varies the band's WIDTH around the circumference rather than its
                // position, so the halo still breathes without ever leaving the edge.
                float churn = 1.0 + (PortalFbm(float2(angle * 3.0, t * 0.4), 3) - 0.5)
                                  * _Churn * 1.2;

                float band = max(_Thickness * _Depth * churn, 1e-4);
                float ringD = _Radius * _Depth;

                // Distance from the ring, which sits ringD metres OUTSIDE the aperture's edge.
                float off = abs(d - ringD);

                float ring = saturate(1.0 - off / band);
                ring = pow(ring, _Falloff);

                // Sparks orbiting the rim. One-dimensional noise in the angle,
                // scrolled — cheap, and it reads as material being dragged
                // around the aperture rather than as twinkling.
                float orbit = frac(angle / (2.0 * PI) + t * _SparkSpeed * 0.05);
                float spark = PortalValueNoise(float2(orbit * _Sparks, t * 1.7));
                spark = pow(saturate(spark), 8.0);

                float radial = saturate(1.0 - abs(d - ringD) / (band * 2.2));
                float glow = ring + spark * radial * 2.0;

                // Fade the whole halo as the aperture closes, and feather it out at the quad's
                // border so the sheet never ends in a visible seam.
                //
                // A BOX, not the inscribed circle this used to be. Measured in quad space the
                // circle reaches 1 only at the corners, so an aperture that ran diagonally — the
                // shape a diagonal sweep paints — had its halo erased at exactly the two ends the
                // paint reached furthest, while the aperture itself carried on. Portal.cs now
                // sizes this quad to the halo's own reach in metres, so the ring is inside this
                // fade at every size and only the last few per cent of the sheet is touched.
                float2 border = abs(p);
                glow *= open * saturate(1.0 - smoothstep(0.93, 1.0, max(border.x, border.y)));

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
