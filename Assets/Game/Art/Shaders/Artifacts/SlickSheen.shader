// The film of frost the cryo sprayer leaves, and the `Slick` SurfaceCoat patch generally
// (Artifacts/CryoSprayer.md, Artifacts/SurfaceCoat.md).
//
// THE MESH IS A UNIT CUBE IN OBJECT SPACE, spanning -0.5..+0.5 on every axis — Unity's own
// Cube primitive, unaltered. It reads POSITION and nothing else: NO UV, NO NORMAL, NO VERTEX
// COLOUR. The patch's transform carries the real footprint (1.2 m radius per dab, per the
// design) and its own thickness decides how much vertical relief the film is allowed to climb.
//
// WHY A DEPTH-REPROJECTED DECAL AND NOT A DISC LYING ON THE GROUND. A flat disc on a dune
// intersects it: half the patch sinks under the sand and the other half floats. Conforming a
// mesh to the terrain at spawn time is the usual fix and it is a mesh job done per dab, on a
// throttled hold stream, on the server. This does it per pixel instead: the fragment samples
// the scene depth under itself, rebuilds the world position of whatever solid is actually
// there, and asks whether that point is inside the patch's box. Ground, a rock, a foam ramp
// and the roof of a lander are all painted correctly and none of them needs to be known
// about. The surface normal comes from the screen-space derivatives of that rebuilt position,
// so the film is lit by the real slope it is lying on.
//
// The box is drawn BACK faces only with ZTest GEqual, the standard decal setup: it covers the
// case of the camera standing inside the patch, which for a 1.2 m puddle on the ground is not
// an edge case but the normal way a player meets one.
//
// WHY IT IS NOT ONLY A GRAZING SHEEN. The design asks for a wet sheen that reads at a grazing
// angle, and says in the same breath that a patch the player cannot see is a defect rather
// than a trap. Those pull against each other, because the angle a player looks at the ground
// from when they are about to walk onto it is the STEEPEST one, which is exactly where a
// grazing-only effect is invisible. So the film has two independent reads:
//
//   • The sheen, view-DEPENDENT: a fresnel-weighted lift plus the iridescence, strongest
//     across the grazing band, which is the look the design asks for.
//   • The wetting, view-INDEPENDENT: the film darkens what it lies on, which is what a wet
//     surface actually does and is available from every angle including straight down.
//
// Neither depends on the sun's azimuth. The one term that does is the glint, and it is
// additive on top of a patch that is already fully legible without it — set _GlintStrength to
// 0 and nothing about finding a patch changes. That is how the "invisible from one half of
// the compass" failure was ruled out here: there is no term the read depends on that has a
// preferred direction in the horizontal plane. (GDC-L1-UX-0003: the hazard has to answer
// "what's happening" at a glance, and GDC-L1-SYS-0006: a surface property the player cannot
// perceive produces superstition, not depth.)
//
// WHAT THE QUANTIZER DOES TO A "FAINT RAINBOW", MEASURED. PastelQuantize snaps every pixel to
// one of 204 palette entries, and below about Oklab C 0.02 there is no coloured entry at all —
// the colour falls onto the grey ramp. A hue sweep authored at C 0.015, which is what "faint"
// would ordinarily mean, collapsed to four near-neutral entries and read as dirty grey. The
// same sweep at C 0.07 hit 11 distinct entries out of 12 bands and reads as a rainbow. So the
// iridescence here is authored at full chroma and made faint by COVERAGE — it occupies a
// narrow grazing band and nothing else — because desaturating it does not make it subtle, it
// deletes it. The bands are generated in Oklch through SubstanceOklchToLinear rather than
// mixed in RGB, which is what aims them at the lattice's vivid ring instead of hoping.
Shader "SpaceGame/Artifacts/SlickSheen"
{
    Properties
    {
        [Header(Footprint)]
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.5)) = 0.05
        _HeightFade   ("Height Fade",   Range(0.05, 1))    = 0.35
        _SlopeLimit   ("Slope Limit",   Range(0, 1))       = 0.35

        // Measured floor, not taste: over lit desert sand the film has to remove 33% of the
        // ground's brightness before the quantizer puts the covered pixel on a different
        // palette entry from the uncovered one. Anything gentler is a patch that is drawn and
        // cannot be seen. Checked against sand, pale sand, rock, dark rock and salt flat;
        // 0.33 is the worst of the five, and the default carries a little over it.
        [Header(Wetting  view independent)]
        _WetDarken ("Wet Darkening", Range(0, 0.8)) = 0.38

        // Likewise measured: an additive lift under +0.08 in linear RGB does not move lit sand
        // off its entry, so a sheen quieter than that is invisible on the commonest ground in
        // the game.
        [Header(Sheen  view dependent)]
        _SheenLift     ("Sheen Lift",       Range(0, 0.6))  = 0.16
        _SheenPower    ("Sheen Power",      Range(0.5, 8))  = 3.5
        _RimBrightness ("Edge Ring",        Range(0, 1))    = 0.45
        _RimWidth      ("Edge Ring Width",  Range(0.01, 0.4)) = 0.09

        [Header(Iridescence)]
        _IridescenceStrength  ("Strength",           Range(0, 1))     = 0.7
        _IridescenceChroma    ("Chroma (Oklch C)",   Range(0, 0.1))   = 0.07
        _IridescenceLightness ("Lightness (Oklch L)",Range(0.4, 0.98))= 0.86
        _IridescenceBands     ("Hue Bands",          Range(3, 16))    = 9
        _IridescenceCycles    ("Hue Cycles",         Range(0.5, 6))   = 2.2
        _IridescenceSwirl     ("Swirl",              Range(0, 2))     = 0.9
        _FilmScale            ("Film Scale (per m)", Range(0.2, 12))  = 2.4
        _FilmDrift            ("Film Drift",         Range(0, 2))     = 0.25

        [Header(Sun glint)]
        _GlintStrength ("Glint Strength", Range(0, 2))    = 0.6
        _GlintTightness("Glint Tightness",Range(8, 256))  = 90

        [Header(Expiry)]
        _Fade ("Fade", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent-100"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SlickSheen"

            // Premultiplied alpha, and it is doing two jobs at once the way
            // RepulsorAirWarp.shader's does. rgb is what the film ADDS — the sheen and the
            // iridescent band — and alpha is how much of the ground underneath it REMOVES.
            // One blend then covers the whole range continuously: alpha alone is the wet
            // darkening, alpha with matching rgb is a straight replacement by the sheen
            // colour, and everything between is the two mixing. Straight SrcAlpha blending
            // could only do the replacement, and a wet surface that does not darken is the
            // half of the read that works from directly above.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest GEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // _CameraDepthTexture and SampleSceneDepth: the whole decal rests on it.
            // PC_RPAsset and Mobile_RPAsset both have m_RequireDepthTexture: 1, so it exists.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "ArtifactSubstance.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _EdgeSoftness;
                float _HeightFade;
                float _SlopeLimit;

                float _WetDarken;

                float _SheenLift;
                float _SheenPower;
                float _RimBrightness;
                float _RimWidth;

                float _IridescenceStrength;
                float _IridescenceChroma;
                float _IridescenceLightness;
                float _IridescenceBands;
                float _IridescenceCycles;
                float _IridescenceSwirl;
                float _FilmScale;
                float _FilmDrift;

                float _GlintStrength;
                float _GlintTightness;

                float _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos  : TEXCOORD0;
                float  fogFactor  : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                // Homogeneous, divided in the fragment: ComputeScreenPos interpolates
                // perspective-correctly, so the divide lands exactly on the pixel this
                // fragment covers. Deriving it from SV_Position instead would need
                // _ScreenParams and would still have to be corrected for dynamic resolution.
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 screenUv = IN.screenPos.xy / max(IN.screenPos.w, 1e-4);

                // Rebuild the world position of whatever solid is behind this pixel. The
                // skybox comes back at the far plane, lands far outside the box, and is
                // discarded by the bounds test below — so the film never paints the sky.
                float rawDepth = SampleSceneDepth(screenUv);
                float3 surfaceWS = ComputeWorldSpacePosition(screenUv, rawDepth, UNITY_MATRIX_I_VP);
                float3 surfaceOS = TransformWorldToObject(surfaceWS);

                // The geometric normal of the surface being painted, from the derivatives of
                // the rebuilt position. Exact for a flat triangle and free — no normal buffer
                // and no assumption about the mesh underneath, which is the whole point of
                // painting through depth rather than onto a fitted disc.
                //
                // TAKEN BEFORE ANY discard, and it has to be. A derivative is evaluated over
                // the 2x2 quad, so if a neighbouring fragment has already been killed the
                // value here is undefined — which shows up as a rim of wrongly-lit pixels
                // around the patch, on some drivers and not others.
                //
                // It is still wrong on the one pixel line where a silhouette crosses the
                // patch, because the two halves of the quad are then on different surfaces
                // metres apart. That is inherent to reading a normal out of depth, it is a
                // pixel wide, and the alternative is a normals buffer this pipeline does not
                // produce — so it is accepted rather than worked around.
                float3 surfaceNormalWS = normalize(cross(ddy(surfaceWS), ddx(surfaceWS)));

                // Inside the patch box? The vertical test is separate from the radial one:
                // a patch sprayed on the ground must not also paint the underside of a ledge
                // half a metre above it just because the box reaches that far.
                float height = abs(surfaceOS.y) * 2.0;             // 0 at mid, 1 at the box face
                float radius = length(surfaceOS.xz) * 2.0;         // 0 at centre, 1 at the box face
                if (height > 1.0 || radius > 1.0)
                {
                    discard;
                }

                // A film sprayed downward does not cling to a vertical face. Fading by slope
                // rather than discarding on it keeps the boundary from crawling as the camera
                // moves, which a hard slope cutoff on a smooth dune does very visibly.
                //
                // It does a second job worth knowing about: a decal read out of depth paints
                // ANYTHING whose depth falls inside the box, including the legs of whoever is
                // standing in the puddle. Legs are near-vertical, so the slope term rejects
                // them, and lowering _SlopeLimit toward 0 to make the film climb a dune also
                // starts painting the people standing on it.
                float slope = saturate((abs(surfaceNormalWS.y) - _SlopeLimit)
                                     / max(1.0 - _SlopeLimit, 1e-4));

                // A HARD radial edge on purpose. PastelQuantize's ink pass draws a line
                // wherever the frame's lightness gradient crosses 0.03 L, so a patch that
                // ends in a step gets outlined by the post pass for free, and one that
                // feathers away over 30 cm does not. The outline is most of what lets a
                // player pick the patch out of the ground at a distance, so the default
                // softness is small and there is a reason not to raise it much.
                float coverage = 1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, radius);
                coverage *= 1.0 - smoothstep(1.0 - _HeightFade, 1.0, height);
                coverage *= slope;
                coverage *= 1.0 - _Fade;

                if (coverage <= 0.002)
                {
                    discard;
                }

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(surfaceWS));

                // World space, so two overlapping dabs share one film pattern and read as one
                // spill rather than as two discs — the same reason the foam samples its
                // bubbles in world space. Drifting slowly, because a perfectly static film on
                // moving ground reads as a printed texture rather than as a liquid.
                float3 filmSample = surfaceWS * _FilmScale
                                  + float3(0.0, _Time.y * _FilmDrift, 0.0);
                float film = SubstanceTurbulence(filmSample);

                // Grazing weight. This is the only place the view direction enters, and it is
                // rotationally symmetric about the surface normal — so the sheen behaves the
                // same from every compass bearing and differs only with how low the eye is.
                float grazing = SubstanceFresnel(surfaceNormalWS, viewDirWS, _SheenPower);

                // A bright ring standing just inside the boundary, so the patch has a hard
                // edge to be outlined by even where the ground it lies on happens to be the
                // same lightness as the film.
                float ring = smoothstep(1.0 - _RimWidth, 1.0 - _RimWidth * 0.35, radius)
                           * (1.0 - smoothstep(1.0 - _RimWidth * 0.35, 1.0, radius));

                float sheen = saturate(grazing * _SheenLift + ring * _RimBrightness);

                // Iridescence. Thin-film colour turns with the angle, so the hue is driven by
                // the same grazing term the sheen is, plus the film's own thickness variation,
                // and then QUANTIZED into a fixed number of hue bands. The banding is the
                // point: the palette holds 16 hues and resolves nothing finer than about 13
                // degrees, so a smooth hue ramp would be snapped into bands anyway — authored
                // bands are bands that can be tuned, emergent ones are just what is left over.
                float hue01 = frac(grazing * _IridescenceCycles + film * _IridescenceSwirl);

                // Plain floor here, NOT SubstanceBand. Hue is cyclic, and SubstanceBand's
                // endpoint-inclusive rounding — which is right for a shading ladder, where
                // both ends have to be reachable — would put band 0 and the last band on the
                // same hue and quietly cost one colour at the wrap.
                float bandedHue = floor(hue01 * _IridescenceBands) / _IridescenceBands;

                // saturate, because Oklch has no gamut fit on this side (see
                // SubstanceOklchToLinear): an out-of-range request comes back with a negative
                // channel, and under premultiplied `Blend One` a negative channel SUBTRACTS
                // from the frame — a black rainbow, which is a memorable way to find out.
                float3 iridescent = saturate(SubstanceOklchToLinear(
                    _IridescenceLightness, _IridescenceChroma, bandedHue * TWO_PI));

                // The sheen's own colour: white at the bottom of the iridescence dial, the
                // banded spectrum at the top. Mixed here, before anything is composited, so
                // the pixel the quantizer sees is the intended colour rather than the
                // intended colour laid over whatever the ground was.
                float3 sheenColour = lerp(float3(1.0, 1.0, 1.0), iridescent, _IridescenceStrength);

                // The sun glint, and the only azimuth-dependent term in the shader. It is a
                // bonus on a patch that is already readable without it — see the header.
                Light mainLight = GetMainLight();
                float3 halfway = normalize(mainLight.direction + viewDirWS);
                float glint = pow(saturate(dot(surfaceNormalWS, halfway)), _GlintTightness)
                            * _GlintStrength;

                float sheenCoverage = saturate((sheen + glint) * coverage);
                float wetCoverage = _WetDarken * coverage;

                // Premultiplied: rgb is what the film adds, alpha is how much of the ground
                // it takes away. Where the sheen is strong the two match and the pixel
                // becomes the sheen colour outright, which is what puts the rainbow band ON a
                // vivid palette entry instead of merely nudging the sand toward one.
                float3 added = sheenColour * sheenCoverage;
                float removed = saturate(wetCoverage + sheenCoverage);

                // Fog attenuates BOTH channels rather than mixing toward the fog colour. With
                // premultiplied alpha, lerping rgb toward the fog colour while leaving alpha
                // alone would keep erasing the same amount of ground while painting fog over
                // it, so a distant patch would go pale but never actually recede. Scaling both
                // is what fades the whole film out. The guard is required: URP's
                // ComputeFogIntensity returns 0, not 1, when no fog keyword is set, which
                // without it would make the patch vanish whenever fog was off.
            #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                float fogIntensity = ComputeFogIntensity(IN.fogFactor);
                added *= fogIntensity;
                removed *= fogIntensity;
            #endif

                return half4(added, removed);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
