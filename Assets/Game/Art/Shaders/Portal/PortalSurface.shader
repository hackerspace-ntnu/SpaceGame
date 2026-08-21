// The portal window.
//
// Two jobs, and the whole design is about not letting either ruin the other:
//
//   1. It is a HOLE. What you see through it is a real render of the world from
//      behind the linked aperture, done by PortalRenderer into a RenderTexture,
//      and it is sampled by SCREEN position rather than by the quad's UVs. The
//      portal camera was posed to match the viewer's eye, so pixel (x, y) of
//      that render is exactly what belongs at pixel (x, y) here; mapping it
//      through mesh UVs would re-project it and the seam would slide as the
//      player moved.
//
//   2. It is ENERGY. A clean window reads as a mirror or a hole in the level
//      geometry, not as something a gun tore open. So the view is graded into a
//      single hue and a swirl of the same hue is laid over it.
//
// ONE HUE, deliberately. An earlier version had an orange aperture and a blue
// one, and two saturated complementary colours fighting each other across the
// same screen is what made it read as clip art. Everything here is yellow: deep
// amber in the throat, gold through the body, white-hot at the rim. The two
// apertures are told apart by VALUE — one warm and deep, one pale and bright —
// not by hue.
//
// Rendered in the Geometry queue writing depth, deliberately. A portal has to
// occlude the wall it is cut into and be occluded by things in front of it, and
// travellers pass through in both directions — a transparent-queue portal sorts
// against every other transparent object in the scene and loses.
Shader "SpaceGame/Portal/PortalSurface"
{
    Properties
    {
        [NoScaleOffset] _PortalTexture ("Portal view", 2D) = "black" {}

        [Header(Colour)]
        _Colour      ("Body colour", Color) = (1.00, 0.78, 0.16, 1)
        _DeepColour  ("Throat colour", Color) = (0.42, 0.20, 0.02, 1)
        _HotColour   ("Hot colour", Color) = (1.00, 0.96, 0.72, 1)

        [Header(View)]
        // How far the far room is dragged into the portal's own hue. 0 is a
        // clean window; 1 is fully graded. Around 0.6 you can still read the
        // room but it is unmistakably seen through something.
        _ViewTint    ("View tint", Range(0.0, 1.0)) = 0.62
        _ViewGain    ("View brightness", Range(0.0, 3.0)) = 1.0

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
        _Energy      ("Energy over view", Range(0.0, 1.0)) = 0.45

        [Header(Distortion)]
        _Refract     ("Edge refraction", Range(0.0, 0.15)) = 0.045
        _Fringe      ("Chromatic fringe", Range(0.0, 0.03)) = 0.006

        // Driven by Portal.cs: the aperture irises open from the middle out.
        _Open        ("Open", Range(0.0, 1.0)) = 1.0
        // 0 when there is no render to show — unlinked, or past the recursion
        // limit. The swirl carries the whole surface on its own.
        _HasView     ("Has view", Range(0.0, 1.0)) = 1.0

        // Diagnostic: returns the raw sampled view with nothing on top. Kept in
        // the shipped shader because "is it showing the wrong thing or nothing
        // at all" comes up every time a portal looks wrong, and answering it by
        // temporarily hacking the shader is how a hack ends up shipping.
        [Toggle] _DebugRawView ("Debug: raw view only", Float) = 0
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PortalNoise.hlsl"

            // Plain TEXTURE2D, not TEXTURE2D_X. The _X macros exist for XR
            // single-pass, where the target is a texture array and the sample
            // needs a slice index. This project is not XR and the target is an
            // ordinary RenderTexture written by an ordinary camera.
            TEXTURE2D(_PortalTexture);
            SAMPLER(sampler_PortalTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _Colour;
                float4 _DeepColour;
                float4 _HotColour;
                float  _ViewTint;
                float  _ViewGain;
                float  _Aperture;
                float  _EdgeWidth;
                float  _EdgeGlow;
                float  _Throat;
                float  _Swirl;
                float  _Speed;
                float  _Scale;
                float  _Warp;
                float  _Energy;
                float  _Refract;
                float  _Fringe;
                float  _Open;
                float  _HasView;
                float  _DebugRawView;
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
                float4 screenPos   : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.screenPos   = pos.positionNDC;
                OUT.uv          = IN.uv;
                return OUT;
            }

            // Push a colour into the portal's single hue, keeping its shape.
            //
            // Grading by LUMINANCE rather than tinting by multiplication is what
            // keeps the far room readable: a multiply would take a blue wall to
            // near-black, where this maps it to a dark amber and a lit wall to
            // gold. The room's contrast survives; only its hue is replaced.
            float3 Grade(float3 source)
            {
                float luma = dot(source, float3(0.299, 0.587, 0.114));

                float3 low  = lerp(_DeepColour.rgb, _Colour.rgb, saturate(luma * 2.0));
                float3 high = lerp(_Colour.rgb, _HotColour.rgb, saturate(luma * 2.0 - 1.0));

                return luma < 0.5 ? low : high;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Aperture space: the quad's UV recentred so the portal is the
                // ellipse inscribed in it, whatever the quad's aspect.
                float2 p = IN.uv * 2.0 - 1.0;
                float  r = length(p);
                float  angle = atan2(p.y, p.x);
                float  t = _Time.y * _Speed;

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
                float edgeR = (_Aperture + crawl * 0.05) * saturate(_Open);

                clip(edgeR - r);

                float rim = saturate((r - (edgeR - _EdgeWidth)) / max(_EdgeWidth, 1e-4));

                // Screen-space sample, bent outward as it nears the rim so the
                // hard cut hides behind a refraction rather than announcing
                // itself as a straight edge.
                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);
                float2 outward = r > 1e-4 ? p / r : float2(0.0, 0.0);
                float2 push = outward * rim * rim * _Refract;

                // Clamped inside the target. A sample at exactly 1.0 indexes one
                // texel past the end of the row and comes back black on some
                // backends, which shows up as a dark band along one edge of
                // every portal.
                float2 uvR = clamp(screenUV + push * (1.0 + _Fringe), 0.0005, 0.9995);
                float2 uvG = clamp(screenUV + push,                   0.0005, 0.9995);
                float2 uvB = clamp(screenUV + push * (1.0 - _Fringe), 0.0005, 0.9995);

                float3 view;
                view.r = SAMPLE_TEXTURE2D(_PortalTexture, sampler_PortalTexture, uvR).r;
                view.g = SAMPLE_TEXTURE2D(_PortalTexture, sampler_PortalTexture, uvG).g;
                view.b = SAMPLE_TEXTURE2D(_PortalTexture, sampler_PortalTexture, uvB).b;
                view *= _ViewGain;

                if (_DebugRawView > 0.5)
                    return half4(view, 1.0);

                float3 graded = lerp(view, Grade(view), saturate(_ViewTint));

                // The energy layer: domain-warped noise in the swirled frame, so
                // it turns with the vortex instead of scrolling past it.
                float churn = PortalWarpedFbm(swirled * _Scale, t, _Warp);
                churn = saturate((churn - 0.42) * 2.4);

                float3 energy = lerp(_Colour.rgb, _HotColour.rgb, saturate(churn * 1.4));

                // Denser towards the rim and thin over the middle, so the view
                // stays legible where the player is actually looking through it.
                float energyMask = saturate(_Energy * (churn * 0.55 + rim * 1.25));

                float3 col = lerp(graded, energy, energyMask);

                // The throat: the middle of the aperture sits deeper, which is
                // what gives a flat disc the feeling of a tunnel mouth.
                col *= lerp(1.0 - _Throat, 1.0, saturate(r * 1.25));

                // Nothing to show — unlinked, or past the recursion limit. The
                // swirl becomes the whole surface rather than a layer on it.
                float3 dead = lerp(_DeepColour.rgb, _Colour.rgb, churn);
                dead = lerp(dead, _HotColour.rgb, saturate(churn * churn * 1.6));
                dead *= lerp(1.0 - _Throat, 1.0, saturate(r * 1.25));

                col = lerp(dead, col, saturate(_HasView));

                // The rim burns over everything, view or not.
                col += _HotColour.rgb * pow(rim, 3.0) * _EdgeGlow;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
