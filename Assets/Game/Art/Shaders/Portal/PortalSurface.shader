// The portal aperture.
//
// It is ENERGY, and only energy. Nothing here knows or cares what is on the
// other side.
//
// It used to. The surface sampled a RenderTexture that PortalRenderer filled
// from a second camera posed behind the linked aperture, so the far room was
// visible through the hole, graded into this shader's hue. That cost a whole
// extra scene render — shadow cascades and all — per aperture per frame, and it
// was cut. What is left is the swirl that used to be drawn OVER that view, now
// standing on its own: deep amber in the throat, gold through the body,
// white-hot at the rim.
//
// ONE HUE, deliberately. An earlier version had an orange aperture and a blue
// one, and two saturated complementary colours fighting each other across the
// same screen is what made it read as clip art. Everything here is yellow. The
// two apertures are told apart by VALUE — one warm and deep, one pale and
// bright — not by hue.
//
// Rendered in the Geometry queue writing depth, deliberately. A portal has to
// occlude the wall it is cut into and be occluded by things in front of it, and
// travellers pass through in both directions — a transparent-queue portal sorts
// against every other transparent object in the scene and loses.
Shader "SpaceGame/Portal/PortalSurface"
{
    Properties
    {
        [Header(Colour)]
        _Colour      ("Body colour", Color) = (1.00, 0.78, 0.16, 1)
        _DeepColour  ("Throat colour", Color) = (0.42, 0.20, 0.02, 1)
        _HotColour   ("Hot colour", Color) = (1.00, 0.96, 0.72, 1)

        [Header(Aperture)]
        _Aperture    ("Aperture", Range(0.5, 1.0)) = 0.97
        _EdgeWidth   ("Edge width", Range(0.01, 0.6)) = 0.22
        _EdgeGlow    ("Edge glow", Range(0.0, 12.0)) = 3.2
        _Throat      ("Throat darkening", Range(0.0, 1.0)) = 0.45

        [Header(Motion)]
        _Swirl       ("Swirl", Range(-8.0, 8.0)) = 2.1
        _Speed       ("Speed", Range(0.0, 4.0)) = 0.55
        _Scale       ("Noise scale", Range(0.5, 12.0)) = 3.1
        _Warp        ("Domain warp", Range(0.0, 3.0)) = 1.15
        [Toggle(_PORTAL_DEEP_SWIRL)] _DeepSwirl ("Deep swirl (costlier)", Float) = 0

        // Driven by Portal.cs: the aperture irises open from the middle out.
        _Open        ("Open", Range(0.0, 1.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry+1"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "PortalSurface"
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            // The domain warp, at full strength, is five fbm evaluations per
            // pixel — comfortably the most expensive thing left in this shader
            // and the only part of it that scales with how much of the screen an
            // aperture fills. One warp instead of two costs about 40% less and
            // the difference is a slightly less folded churn. Off by default;
            // the switch is on the material for anyone who wants to spend it.
            #pragma shader_feature_local_fragment _PORTAL_DEEP_SWIRL

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PortalNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Colour;
                float4 _DeepColour;
                float4 _HotColour;
                float  _Aperture;
                float  _EdgeWidth;
                float  _EdgeGlow;
                float  _Throat;
                float  _Swirl;
                float  _Speed;
                float  _Scale;
                float  _Warp;
                float  _DeepSwirl;
                float  _Open;
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
                OUT.uv          = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Aperture space: the quad's UV recentred so the portal is the
                // ellipse inscribed in it, whatever the quad's aspect.
                float2 p = IN.uv * 2.0 - 1.0;
                float  r = length(p);

                // Conservative reject BEFORE any noise is evaluated. The
                // crawling rim below can only move the edge by ±0.025, so
                // anything past that is outside the aperture whatever the noise
                // turns out to say. The quad's corners are a fifth of its area
                // and none of them are ever inside the disc; making them pay for
                // an fbm to find that out was the cheapest waste in the shader.
                float openness = saturate(_Open);
                clip((_Aperture + 0.03) * openness - r);

                float angle = atan2(p.y, p.x);
                float t = _Time.y * _Speed;

                // Swirl: rotate the sampling frame by an amount that grows
                // towards the centre. Rotating by a CONSTANT would just spin the
                // whole disc like a wheel; making the rotation depend on radius
                // is what shears it into a vortex.
                float twist = angle + _Swirl * (1.0 - saturate(r)) + t * 0.6;
                float2 swirled = float2(cos(twist), sin(twist)) * r;

                // The rim is not a clean circle — it crawls. Sampled in polar
                // coordinates so the wobble travels AROUND the rim rather than
                // sliding across it.
                float crawl = PortalFbm(float2(angle * 2.2, t * 0.6), 3) - 0.5;
                float edgeR = (_Aperture + crawl * 0.05) * openness;

                clip(edgeR - r);

                float rim = saturate((r - (edgeR - _EdgeWidth)) / max(_EdgeWidth, 1e-4));

                // Domain-warped noise in the SWIRLED frame, so it turns with the
                // vortex instead of scrolling past it.
            #ifdef _PORTAL_DEEP_SWIRL
                float churn = PortalWarpedFbm(swirled * _Scale, t, _Warp);
            #else
                float churn = PortalWarpedFbmLite(swirled * _Scale, t, _Warp);
            #endif

                // Two ramps rather than one, so the substance has a dark body
                // and sparse hot filaments through it rather than a flat mid
                // tone. Squaring the second is what keeps the white rare.
                float3 col = lerp(_DeepColour.rgb, _Colour.rgb, churn);
                col = lerp(col, _HotColour.rgb, saturate(churn * churn * 1.6));

                // The throat: the middle of the aperture sits deeper, which is
                // what gives a flat disc the feeling of a tunnel mouth.
                col *= lerp(1.0 - _Throat, 1.0, saturate(r * 1.25));

                // The rim burns over everything.
                col += _HotColour.rgb * pow(rim, 3.0) * _EdgeGlow;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
