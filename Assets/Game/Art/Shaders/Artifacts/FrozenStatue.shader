// The ice a body is frozen into by the Cryo Sprayer (Artifacts/CryoSprayer.md, and the
// `Frozen` kind in Artifacts/StatusEffects.md).
//
// THE MESH IS WHATEVER WAS FROZEN. That is the point of the effect and it is also the reason
// this shader assumes nothing about the mesh beyond POSITION and NORMAL: it goes on a statue
// prefab posed from a live body, which may be a skinned character, a crate or a six-legged
// crawler. IT READS NO UV AND NO VERTEX COLOUR. All of its surface detail is triplanar, so a
// rig whose UVs are packed, mirrored, missing or laid out for a completely different material
// is shaded identically. That is not a convenience; a statue with a black patch where an
// atlas seam used to be is the failure this avoids.
//
// WHY THE SILHOUETTE SURVIVES, WHICH IS THE ONE THING THE DESIGN INSISTS ON. Two independent
// mechanisms, because ice against a pale sky is the case where either alone is thin:
//
//   • It is drawn OPAQUE, in the Geometry queue, with ZWrite on and a DepthOnly pass.
//     PC_Renderer.asset has m_CopyDepthMode: 0 (after opaques), so only opaque geometry
//     reaches _CameraDepthTexture — and PastelQuantize's ink pass draws its silhouettes from
//     the depth gradient it finds there. An opaque statue is therefore outlined by the post
//     pass for nothing. A genuinely alpha-blended one would be in the Transparent queue,
//     would be absent from that texture, and would lose the outline with no error anywhere.
//   • The rim term brightens the thin edges, which is both what light through thin ice
//     actually does and a second, ink-independent edge.
//
// So the translucency is SIMULATED rather than blended: a wrapped diffuse that carries light
// past the terminator, a rim that lights the thin edges, internal ridged flaws that read as
// fracture planes, and a darkening through the thick middle. Thickness is a lighting property
// here, as it is for the foam and for PortalGoo's droplets. Refracting the actual scene
// behind the statue would look better in a still and would cost the outline, because a scene
// colour sample is only available in the Transparent queue. The outline is worth more.
// (GDC-L1-UX-0003: readability is ranked by contrast, and the silhouette is the contrast that
// answers "what did I freeze". GDC-L1-TECH-0004: the coherent stylized read beats the more
// faithful one the frame cannot hold anyway.)
//
// THE FREEZE IS TELEGRAPHED. `_Freeze` runs 0..1 and rime creeps over the body as it goes, so
// the moment a creature stops being a creature is a second and a half of visible change
// rather than one frame of substitution (GDC-L1-FEEL-0004 — a meaningful event gets an
// acknowledgement). Drive it from the same replicated progress the design already says the
// build-up is driven from, and every machine sees the same freeze arriving.
Shader "SpaceGame/Artifacts/FrozenStatue"
{
    Properties
    {
        // The four bands ARE the material's whole colour range, authored ON palette entries
        // (#AEE1F5 #94C3EF #6BA4D8 #477FB1 — hue 250 degrees, Oklab L 0.90 / 0.80 / 0.69 /
        // 0.57) so the quantizer's snap is a no-op on them. Checked against
        // tools/palette_golden.txt: four distinct entries, all four in the same blue column,
        // none of them falling onto the grey ramp, and all four distinct from the entries
        // that lit sand, shaded rock and the pale desert sky land on.
        //
        // Do not raise the top band's lightness much past 0.90 at this chroma: above it the
        // colour leaves sRGB, gets clamped, and comes back a different HUE — cyan rather than
        // blue — which is the mechanism PastelPalette.FitChroma exists to avoid, and there is
        // no FitChroma on this side.
        [Header(Shading ladder)]
        _BandLit   ("Band 1  Lit",   Color) = (0.682, 0.882, 0.961, 1)
        _BandUpper ("Band 2  Upper", Color) = (0.580, 0.765, 0.937, 1)
        _BandLower ("Band 3  Lower", Color) = (0.420, 0.643, 0.847, 1)
        _BandDeep  ("Band 4  Deep",  Color) = (0.278, 0.498, 0.694, 1)

        [Header(Translucency)]
        _LightWrap  ("Light Wrap",      Range(0, 1))   = 0.85
        _Ambient    ("Ambient Floor",   Range(0, 1))   = 0.3
        _RimLift    ("Thin Edge Lift",  Range(0, 1))   = 0.5
        _RimPower   ("Thin Edge Power", Range(0.5, 8)) = 2
        _CoreDarken ("Core Darkening",  Range(0, 1))   = 0.35

        [Header(Internal flaws)]
        _FlawScale     ("Flaw Scale (per m)",    Range(1, 40))  = 7
        _FlawDepth     ("Flaw Depth",            Range(0, 1))   = 0.4
        _FlawContrast  ("Fracture vs Cloud",     Range(0, 1))   = 0.7
        _FlawSharpness ("Triplanar Sharpness",   Range(1, 16))  = 4

        [Header(Facet glint)]
        _GlintStrength  ("Glint Strength",  Range(0, 2))   = 0.8
        _GlintTightness ("Glint Tightness", Range(8, 256)) = 60

        [Header(Freezing)]
        _Freeze       ("Freeze",              Range(0, 1))    = 1
        _FreezeMargin ("Freeze Margin",       Range(0, 0.5))  = 0.05
        _FreezeScale  ("Rime Scale (per m)",  Range(0.2, 12)) = 1.8
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
            #include "FrozenStatue.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalOS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 normalWS   : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                // Object space is carried through as well as world, because the flaws are
                // projected in object space and the lighting is done in world space. Cheaper
                // than inverting the transform per fragment, and correct on a skinned rig
                // where the object-space position is the posed one.
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS   = IN.normalOS;
                OUT.fogFactor  = ComputeFogFactor(positions.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 normalOS = normalize(IN.normalOS);
                FrozenRimeClip(IN.positionOS, normalOS);

                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                float flaws = FrozenFlaws(IN.positionOS, normalOS);

                // The flaws bend the normal as well as shading it, which is what gives the
                // surface facets for the glint to catch. Bending along the view direction
                // approximates the field's gradient closely enough at this scale, and once
                // the result has been banded into four flats the difference from a real
                // three-tap derivative does not survive to the screen.
                normalWS = normalize(normalWS + (flaws - 0.5) * _FlawDepth * viewDirWS);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // EVERYTHING drives ONE SCALAR and the colour comes only out of the four-entry
                // ladder, the same discipline the foam follows: move the value before the
                // snap, never composite after it. It is what makes the statue on-palette by
                // construction. The sun's colour deliberately does not tint the ice — a tint
                // walks the four bands off the blue column they were picked from, and the
                // quantizer then scatters them across the lattice.
                float diffuse = SubstanceWrapDiffuse(normalWS, mainLight.direction, _LightWrap);
                float shade = diffuse * mainLight.shadowAttenuation;
                shade = lerp(_Ambient, 1.0, shade);

                // Thick in the middle, thin at the edges: the eye reads a body that is bright
                // at its silhouette and dark through its bulk as something light passes
                // through, which is the whole trick standing in for real translucency here.
                float thickness = 1.0 - SubstanceFresnel(normalWS, viewDirWS, 1.0);
                shade -= _CoreDarken * thickness;
                shade += _RimLift * SubstanceFresnel(normalWS, viewDirWS, _RimPower);

                // Internal fracture planes, darkening the ice where a crack runs through it.
                shade -= (1.0 - flaws) * _FlawDepth * 0.5;

                float3 halfway = normalize(mainLight.direction + viewDirWS);
                shade += _GlintStrength
                       * pow(saturate(dot(normalWS, halfway)), _GlintTightness)
                       * mainLight.shadowAttenuation;

                float3 colour = SubstanceLadder4(shade,
                    _BandDeep.rgb, _BandLower.rgb, _BandUpper.rgb, _BandLit.rgb);

                colour = MixFog(colour, IN.fogFactor);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        // A statue is a solid object standing in the sun and it casts like one. A frozen
        // creature with no shadow reads as a decal of a creature.
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
            #include "FrozenStatue.hlsl"

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalOS   : TEXCOORD1;
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS   = IN.normalOS;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));
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
                FrozenRimeClip(IN.positionOS, normalize(IN.normalOS));
                return 0;
            }
            ENDHLSL
        }

        // This is the pass that puts the statue into _CameraDepthTexture, which is where
        // PastelQuantize's ink finds the silhouette it draws around it — half of why the
        // design's "you can tell what you froze" holds. Deleting it costs the outline and
        // nothing reports it.
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
            #include "FrozenStatue.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalOS   : TEXCOORD1;
            };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS   = IN.normalOS;
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target
            {
                FrozenRimeClip(IN.positionOS, normalize(IN.normalOS));
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
