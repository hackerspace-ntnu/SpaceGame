// The foam the foam gun throws: the jet in front of the muzzle, and the gob a landed dab bursts
// back out of the surface (Artifacts/FoamGun.md).
//
// IT IS FOAM, NOT BUBBLES, AND THAT IS THE WHOLE BRIEF. This shader was first written as a soap
// bubble — a thin translucent shell with a bright silhouette, a wet specular pop and thin-film
// iridescence — and every one of those is a cue that says "bubble" to the eye. They are all gone,
// deliberately, and the four things that replaced them are what say "foam" instead:
//
//   1. THE SILHOUETTE IS BITTEN, NOT ROUND. A perfect circle is the single strongest bubble cue
//      there is; a hundred of them is a ball pit. The disc is eaten into by the same noise field
//      that textures the surface, so every clump has an irregular, lumpy outline and no two are
//      the same shape. The shading normal stays spherical underneath — the lump still lights like
//      a rounded mass, it just is not a drawn circle.
//   2. IT IS OPAQUE. Foam is a dense pile of scattering cells; you cannot see through it. A
//      translucent shell reads as soap however it is shaded.
//   3. THE EDGE GOES DARK, NOT BRIGHT. A thin film is brightest where you look through the most of
//      it, which is the silhouette — that rim lift is the bubble's signature. A dense scattering
//      mass does the opposite, so the grazing edge is DARKENED here and the clump reads as solid.
//   4. THE SURFACE HAS CELLS. Foam is visibly made of pockets. A turbulence field modulates the
//      shading scalar so each clump breaks into light and dark pockets rather than being a smooth
//      lit ball.
//
// There is no specular highlight and no iridescence. Do not add either back "for a bit of life" —
// they are precisely what made this read as bubble gum, twice.
//
// EACH QUAD IS STILL RAY TRACED INTO A SPHERE. The classic billboard impostor (GPU Gems 3, ch. 21
// "True Impostors"; Ray Tracing Gems II, ch. 28): the fragment maps the quad's UV to the unit disc
// and reconstructs the surface normal analytically as
//
//     n = float3(p.xy, sqrt(1 - dot(p.xy, p.xy)))
//
// That is what gives a clump real rounded lighting for the cost of two triangles — far cheaper
// than the sphere meshes this replaced. The bite above is applied to the CLIP only, so the
// silhouette is irregular while the lighting stays smooth.
//
// THE COLOUR IS ON-PALETTE BY CONSTRUCTION. Everything is snapped per pixel to the nearest of the
// committed colours by PastelQuantize (Environment.md), so this drives ONE SCALAR into the same
// four authored bands FoamSurface uses — the sprayed foam and the lump it becomes are then
// visibly one substance. Nothing is ever composited after the band pick; see ArtifactSubstance.hlsl
// for why a ladder and not a ramp, and why a tint here would walk the colour off its palette
// column.
//
// PER-CLUMP VARIATION RIDES THE VERTEX STREAM. `TEXCOORD0.zw` is the ParticleSystem's StableRandom,
// which the builder switches on with SetActiveVertexStreams. Without that stream the spray still
// draws — every clump simply gets the same shape and the same shade, which is the uniform look
// this exists to prevent. If the foam suddenly looks repetitive, check that stream first.
Shader "SpaceGame/Artifacts/FoamSpray"
{
    Properties
    {
        // The same four entries FoamSurface bands into, for the same reason: #FBEECB #E3D7B6
        // #C9BD9D #A99E7F, one warm column of the lattice, hue and chroma held fixed down the
        // ladder so the bands walk down one column instead of wandering onto the grey ramp.
        [Header(Shading ladder)]
        _BandLit    ("Band 1  Lit",    Color) = (0.984, 0.933, 0.796, 1)
        _BandUpper  ("Band 2  Upper",  Color) = (0.890, 0.843, 0.714, 1)
        _BandLower  ("Band 3  Lower",  Color) = (0.788, 0.741, 0.616, 1)
        _BandShadow ("Band 4  Shadow", Color) = (0.663, 0.620, 0.498, 1)

        [Header(Light)]
        _LightWrap    ("Light Wrap",    Range(0, 1)) = 0.55
        _AmbientFloor ("Ambient Floor", Range(0, 1)) = 0.16

        // Grazing angles go DARKER. The opposite of a bubble's rim, and one of the four things
        // that make this read as a dense mass rather than as a shell — see the file header.
        _EdgeShade ("Edge Shading", Range(0, 1)) = 0.35

        [Header(Foam cells)]
        // The pockets foam is visibly made of. Scale is per unit of the quad, so a clump holds its
        // cell size as it swells rather than the pattern inflating with it.
        _CellScale ("Cell Scale",  Range(1, 24))  = 7
        _CellDepth ("Cell Depth",  Range(0, 1))   = 0.45

        // How far the noise eats into the disc. Zero is a perfect circle, which is a bubble.
        _EdgeBite  ("Edge Bite",   Range(0, 0.8)) = 0.42

        [Header(Per clump variation)]
        // How far a clump's own random may bias it along the shading ladder. One band is about
        // 0.33 of the scalar, so the default moves most clumps off the band their lighting alone
        // would have put them on.
        _ShadeJitter ("Shade Jitter", Range(0, 0.5)) = 0.28

        [Header(Body)]
        // Foam is opaque. This exists to be turned DOWN for a thinner outer layer, never up into
        // translucency — see the file header.
        _Opacity ("Opacity", Range(0, 1)) = 1

        [Header(Soft particles)]
        // Without this a clump meeting the ground cuts across it as a hard edge, which is the
        // impostor announcing that it is a flat quad after all.
        _SoftFade ("Soft Fade (m)", Range(0.001, 2)) = 0.35
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
            Name "FoamSpray"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            // The torch, the same way FoamSurface takes it. Foam is sprayed in caves as often as on
            // a dune, and without this the jet is lit only by a sun that is not reaching it.
            #include "../Effects/Flashlight.hlsl"
            #include "ArtifactSubstance.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BandLit;
                half4 _BandUpper;
                half4 _BandLower;
                half4 _BandShadow;

                float _LightWrap;
                float _AmbientFloor;
                float _EdgeShade;

                float _CellScale;
                float _CellDepth;
                float _EdgeBite;

                float _ShadeJitter;
                float _Opacity;
                float _SoftFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                // xy = the quad's UV. zw = the particle's StableRandom, when the emitter sends it
                // (see the file header). Zero is a legal value and means "no variation".
                float4 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float4 uv          : TEXCOORD0;
                float4 color       : COLOR;
                float3 positionWS  : TEXCOORD1;
                float4 screenPos   : TEXCOORD2;
                float  fogFactor   : TEXCOORD3;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);

                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.screenPos  = ComputeScreenPos(positions.positionCS);
                OUT.uv         = IN.uv;

                // The system's own colour and colour-over-lifetime ride the vertex stream, which is
                // how a fade reaches the pixel without this shader knowing what a lifetime is.
                OUT.color     = IN.color;
                OUT.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 p = IN.uv.xy * 2.0 - 1.0;
                float r2 = dot(p, p);

                float rnd = IN.uv.z;
                float rnd2 = IN.uv.w;

                // ── The cells ───────────────────────────────────────────────────
                // Sampled in the quad's OWN UV, offset per clump, so the pattern is fixed to the
                // clump and does not crawl across it as it flies. This one field does two jobs:
                // it eats the silhouette and it pockets the surface, which is why they agree.
                float cells = SubstanceTurbulence(float3(IN.uv.xy * _CellScale, rnd2 * 23.7));

                // ── The bitten silhouette ───────────────────────────────────────
                // Applied to the CLIP only. The lighting normal below stays spherical, so a clump
                // still reads as a rounded mass — it simply is not a drawn circle.
                clip(1.0 - r2 - _EdgeBite * (1.0 - cells));

                float facing = sqrt(saturate(1.0 - r2));
                float3 normalVS = float3(p, facing);

                // Into world space through the inverse view matrix. The camera has no scale, so the
                // upper 3x3 is a pure rotation and the normal survives it without renormalising.
                float3 normalWS = mul((float3x3)UNITY_MATRIX_I_V, normalVS);

                // ── One scalar, the way every substance in this game shades ──────
                Light mainLight = GetMainLight();

                float shade = SubstanceWrapDiffuse(normalWS, mainLight.direction, _LightWrap);

                float3 lamp = SampleFlashlight(IN.positionWS, normalWS, _LightWrap);
                shade += max(lamp.r, max(lamp.g, lamp.b));

                shade = lerp(_AmbientFloor, 1.0, saturate(shade));

                // Dense, not thin: the grazing edge loses light instead of gaining it. This is the
                // term a bubble has the other way round.
                shade -= _EdgeShade * (1.0 - facing);

                // The pockets. What makes a clump read as a piece of foam rather than a lit ball.
                shade += (cells - 0.5) * _CellDepth;

                // Each clump sits a little further up or down the ladder than its lighting alone
                // would put it, so a mass of them is not one flat moulded shape.
                shade += (rnd - 0.5) * 2.0 * _ShadeJitter;

                float3 colour = SubstanceLadder4(saturate(shade),
                    _BandShadow.rgb, _BandLower.rgb, _BandUpper.rgb, _BandLit.rgb);

                // ── Body ────────────────────────────────────────────────────────
                float alpha = _Opacity * IN.color.a;

                // Soft particles. Fade where the clump meets what is behind it, or the silhouette
                // turns back into a hard-edged quad exactly where it touches the ground.
                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-4);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                alpha *= saturate((sceneDepth - IN.screenPos.w) / max(_SoftFade, 1e-3));

                if (alpha <= 0.004) discard;

                colour *= IN.color.rgb;
                colour = MixFog(colour, IN.fogFactor);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
