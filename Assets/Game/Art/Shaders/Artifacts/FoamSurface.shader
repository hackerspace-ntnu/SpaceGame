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
        _BubbleScale     ("Bubble Scale (per m)", Range(1, 40)) = 11
        _BubbleDepth     ("Bubble Depth",         Range(0, 1))  = 0.45
        _BubbleSharpness ("Triplanar Sharpness",  Range(1, 16)) = 4

        [Header(Merging)]
        _WeldRadius ("Weld Radius (m)", Range(0.01, 1.5)) = 0.35

        [Header(Expiry)]
        _Dissolve       ("Dissolve",        Range(0, 1))   = 0
        _DissolveMargin ("Dissolve Margin", Range(0, 0.5)) = 0.05
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
                OUT.fogFactor  = ComputeFogFactor(positions.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 geometricNormal = normalize(IN.normalWS);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                float bubbles = FoamBubbles(IN.positionWS, geometricNormal);
                FoamDissolveClip(bubbles);

                float3 normalWS = FoamWeldedNormal(IN.positionWS, geometricNormal);

                // The bubble field perturbs the welded normal rather than replacing it: the
                // roughness is what stops the mass reading as blown plastic, and applying it
                // after the weld lets it ride over the fillet instead of being smoothed away
                // by it. Bent along the view direction by the field's own value, which at
                // this scale is indistinguishable from a real derivative once the result has
                // been banded into four flats — and costs no extra taps.
                normalWS = normalize(normalWS + (bubbles - 0.5) * _BubbleDepth * viewDirWS);

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

                float3 colour = SubstanceLadder4(shade,
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

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);

                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(OUT.positionWS, OUT.normalWS, _LightDirection));
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
                FoamDissolveClip(FoamBubbles(IN.positionWS, normalize(IN.normalWS)));
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

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target
            {
                FoamDissolveClip(FoamBubbles(IN.positionWS, normalize(IN.normalWS)));
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
