// The foam the Foam Gun sprays (Artifacts/FoamGun.md).
//
// THE MESH IS A UNIT SPHERE IN OBJECT SPACE, and that is the only thing this shader assumes
// about it. It reads POSITION and NORMAL. IT READS NO UV AND NO VERTEX COLOUR, so nothing
// here can break on a mesh whose UV convention turns out to differ from what was expected.
// The blob's transform carries the real radius (0.45 m grown over 0.4 s, per the design), and
// the growth is the transform's scale rather than a shader property — the collider has to
// grow with it, so there is only one place that number can live.
//
// WHY OPAQUE, WHEN THE DESIGN SAYS TRANSLUCENT. Two reasons, and the second is decisive.
//
//   Twenty-four alpha-blended spheres piled on one another is not translucency, it is fog:
//   each layer averages toward the same value and the mass loses its shape entirely.
//   Thickness is a LIGHTING property, not an opacity one — the conclusion PortalGoo.shader
//   reached about its droplets, for the same reason. So the translucency here is built out of
//   a wrapped diffuse that lets light past the terminator and a rim that brightens the thin
//   edges where light really would come through, and the geometry stays opaque and occludes
//   properly.
//
//   And an opaque blob is in _CameraDepthTexture. PC_Renderer.asset has m_CopyDepthMode: 0
//   (after opaques), so ONLY opaque geometry reaches that texture, and PastelQuantize's ink
//   pass draws its silhouettes from the depth gradient it finds there. An opaque foam mass is
//   therefore outlined by the post pass for free; a transparent one is not, and a near-white
//   mass on pale sand with no outline is exactly what reads as a smear rather than as
//   something you can climb. That free outline is worth more than any amount of real alpha
//   (GDC-L1-UX-0003 — rank by contrast, and here the silhouette IS the contrast that says
//   "standable").
//
// HOW THE BLOBS MERGE. Overlapping spheres read as a pile of balls because of the CREASE
// where two surfaces meet: the normal jumps, and the eye reads a jump in the normal as two
// objects. A neighbour-aware smooth-union field fixes precisely that. Each fragment evaluates
// the polynomial smooth-minimum of every nearby blob's sphere distance and shades with the
// blended GRADIENT instead of with its own mesh normal, so at a seam the normal sweeps
// continuously from one sphere into the next and the crease becomes a fillet.
//
// THE ONE INTEGRATION CONTRACT. That field needs the neighbours. `_FoamBlobs` is a global
// float4 array — xyz world centre, w world radius — and `_FoamBlobCount` says how many of it
// are live; both are declared in FoamSurface.hlsl. When nothing sets them the count is 0 and
// every blob falls back to its own mesh normal, which is a correctly shaded sphere: this is
// never black and never wrong, it just does less. The uploader should send the blobs nearest
// the camera and nothing else — the array is bounded and the per-fragment cost is linear in
// the count.
Shader "SpaceGame/Artifacts/FoamSurface"
{
    Properties
    {
        // The four bands ARE the material's whole colour range, and they are authored ON
        // palette entries (#FBEECB #E3D7B6 #C9BD9D #A99E7F — hue 90 degrees, Oklab L 0.95 /
        // 0.88 / 0.80 / 0.72) so the quantizer's snap is a no-op on them. Checked against
        // tools/palette_golden.txt: four distinct entries, all in the same warm column, none
        // falling onto the grey ramp, and none of them the entry lit desert sand lands on.
        // Retuning them means re-checking that — drop a band much below L 0.72 and it leaves
        // the warm column for the grey ramp, and the foam starts reading dirty rather than
        // off-white.
        [Header(Shading ladder)]
        _BandLit    ("Band 1  Lit",    Color) = (0.984, 0.933, 0.796, 1)
        _BandUpper  ("Band 2  Upper",  Color) = (0.890, 0.843, 0.714, 1)
        _BandLower  ("Band 3  Lower",  Color) = (0.788, 0.741, 0.616, 1)
        _BandShadow ("Band 4  Shadow", Color) = (0.663, 0.620, 0.498, 1)

        [Header(Translucency)]
        _LightWrap    ("Light Wrap",      Range(0, 1))   = 0.75
        _AmbientFloor ("Ambient Floor",   Range(0, 1))   = 0.22
        _RimLift      ("Thin Edge Lift",  Range(0, 1))   = 0.35
        _RimPower     ("Thin Edge Power", Range(0.5, 8)) = 2.5
        _Backlight    ("Backlight Bleed", Range(0, 1))   = 0.4

        [Header(Bubbles)]
        // Cells, not blotches. The scale is the bubble PITCH in metres: 14 per metre is a 7 cm
        // bubble, which is the size that still reads as a bubble on a metre lump at the range
        // foam is looked at from.
        _BubbleScale     ("Bubble Scale (per m)", Range(1, 40)) = 14
        // How far a bubble domes the normal, at its steepest — which is at the wall, because
        // the tilt is ramped by the distance to the crown. Reached only at the wall, so this is
        // a peak rather than an average: the middle of a bubble is always near-flat, which is
        // what a dome is.
        _BubbleDepth     ("Bubble Depth",         Range(0, 1))  = 0.7
        // The dark wall between bubbles, subtracted from the shading scalar before the snap so
        // a seam lands a whole band down. This is the single strongest "that is foam" cue there
        // is — a mass of pale domes with no seams between them is polystyrene.
        _BubbleShade     ("Bubble Seam Shade",    Range(0, 1))  = 0.4

        [Header(Merging)]
        _WeldRadius ("Weld Radius (m)", Range(0.01, 1.5)) = 0.35

        [Header(Expiry)]
        _Dissolve       ("Dissolve",        Range(0, 1))   = 0
        _DissolveMargin ("Dissolve Margin", Range(0, 0.5)) = 0.05

        [Header(Per blob variation)]
        // Set per blob from a property block, never authored here. See FoamSurface.hlsl.
        _ShadeBias ("Shade Bias", Range(-0.5, 0.5)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "Queue"           = "Geometry"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend Off
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // The helmet lamp, the same way StylizedTerrain, AlgaeRock and CaveTriplanar take
            // it. Foam is built indoors and underground as often as it is on a dune, and
            // without this a ramp in a cave is lit only by the sun that is not reaching it.
            #include "../Effects/Flashlight.hlsl"
            #include "FoamSurface.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);

                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 geometricNormal = normalize(IN.normalWS);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                SubstanceCellField cells = FoamCells(IN.positionWS);
                FoamDissolveClip(cells);

                float3 normalWS = FoamWeldedNormal(IN.positionWS, geometricNormal);

                // The bubbles, domed onto whatever that left. `uphill` points at the crown of the
                // cell this fragment sits on, so subtracting it tilts the normal away from that
                // crown. A real gradient, unlike the view-space swing this replaced, so the bumps
                // hold still when the camera moves.
                //
                // SCALED BY `wall`, AND THAT FACTOR IS THE WHOLE DIFFERENCE BETWEEN A DOME AND A
                // FACET. `uphill` is a unit vector: a distance field has slope 1 everywhere, so
                // using it raw tilts every fragment in a cell by the SAME angle and only varies
                // the direction — which is a cone, not a dome. Lit by one light and snapped to
                // four bands, a cone lands entirely on one band and the lump comes out as one
                // flat plate per bubble. Multiplying by the distance to the crown makes the
                // profile a paraboloid instead: zero tilt at the crown, steepest at the wall.
                normalWS = normalize(normalWS - cells.uphill * (_BubbleDepth * cells.wall));

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // EVERYTHING drives ONE SCALAR, and the colour only ever comes out of the
                // four-entry ladder. That is the same discipline PastelQuantize's own ink
                // pass follows — move the value BEFORE the snap, never composite after it —
                // and it is what makes the foam on-palette by construction rather than by
                // luck. The corollary is deliberate: the sun's COLOUR does not tint the foam,
                // because tinting walks the bands off the hue column the four of them were
                // chosen to sit in and the quantizer then scatters them across the lattice.
                float diffuse = SubstanceWrapDiffuse(normalWS, mainLight.direction, _LightWrap);
                float shade = diffuse * mainLight.shadowAttenuation;

                // The lamp folded into the same scalar, by its brightest channel. Peak rather
                // than a luminance weighting because this is a "how lit is this" question and
                // not a colour conversion — the lamp's warm white undersells itself badly
                // under luma weights.
                float3 lamp = SampleFlashlight(IN.positionWS, normalWS, _LightWrap);
                shade += max(lamp.r, max(lamp.g, lamp.b));

                shade = lerp(_AmbientFloor, 1.0, saturate(shade));

                // Light arriving through the thin parts, in two halves: the rim, where the
                // substance is thinnest against the eye, and the backlight, which is the sun
                // behind the mass pushing through it. Together they are what makes something
                // read as translucent without it ever being transparent.
                shade += _RimLift * SubstanceFresnel(normalWS, viewDirWS, _RimPower);
                shade += _Backlight
                       * pow(saturate(dot(-mainLight.direction, viewDirWS)), 3.0)
                       * SubstanceFresnel(normalWS, viewDirWS, 1.0);

                // The wall between two bubbles, darkened. Light entering a foam gets out again
                // from the crown of a bubble and does not from the crevice three of them make,
                // so the seams are where the shape of the surface is actually readable — a
                // field of pale domes with no seams is polystyrene. Subtracted BEFORE the snap
                // like everything else here, so a seam lands a whole band down rather than
                // being averaged away into the band its bubble is on.
                shade -= _BubbleShade * smoothstep(0.3, 0.95, cells.wall);

                // This blob's own place on the ladder, so a mass of them is not one flat
                // moulded shape. Added to the scalar BEFORE the snap, which is the same
                // discipline as everything above it — move the value, never composite after.
                shade += _ShadeBias;

                float3 colour = SubstanceLadder4(saturate(shade),
                    _BandShadow.rgb, _BandLower.rgb, _BandUpper.rgb, _BandLit.rgb);

                colour = MixFog(colour, IN.fogFactor);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        // Foam is standable geometry, so it casts like geometry. Without this a ramp of it
        // sits on the dune with no shadow and reads as painted on.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "FoamSurface.hlsl"

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            // The normal is a LOCAL here, not a varying: the fragment only clips, and the cell
            // field it clips against is a function of world position alone.
            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(OUT.positionWS, normalWS, _LightDirection));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target
            {
                FoamDissolveClip(FoamCells(IN.positionWS));
                return 0;
            }
            ENDHLSL
        }

        // This is the pass that puts foam into _CameraDepthTexture, which is where
        // PastelQuantize's ink finds the silhouette it draws around the mass. Deleting it
        // costs the outline and nothing reports it.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FoamSurface.hlsl"

            // No NORMAL: this pass writes depth and clips, and the cell field it clips against is
            // a function of world position alone. The shadow pass still reads one, for the bias.
            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);

                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target
            {
                FoamDissolveClip(FoamCells(IN.positionWS));
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
