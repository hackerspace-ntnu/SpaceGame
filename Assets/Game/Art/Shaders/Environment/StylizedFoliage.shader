// Cozy, painterly trees and bushes to stand in GrassField's meadows.
//
// The companion of StylizedGrass and built on the same choices: leaves are opaque geometry
// (no alpha test, so early-z stays cheap however much they overlap), every leaf is lit by the
// normal of the canopy blob it belongs to rather than its own, and all lighting is per vertex
// with ONE hard shadow tap per pixel. Unlike the grass, foliage casts shadows: a tree without
// its shadow floats.
//
// The wind is the grass's wind — GrassField publishes it per camera — so a tree leans with
// the wave rolling through the meadow around it. Vertex data layout: StylizedFoliage.hlsl.
//
// One shader for leaves and bark: bark is a material with no flutter, no accents and its
// gradient running up the trunk (_HeightWeight 1, _ShellWeight 0), culled on its back faces.
// Bark may add a painted bark map (_BARK_TEXTURE): it shades the material's own colour, so a
// palette or colour variant still decides the hue.

Shader "SpaceGame/StylizedFoliage"
{
    Properties
    {
        [Header(Colour)]
        _InnerColor    ("Inner / Root", Color) = (0.13, 0.22, 0.09, 1)
        _OuterColor    ("Outer / Top", Color) = (0.38, 0.52, 0.22, 1)
        _ShellWeight   ("Gradient: Inside to Outside", Range(0, 1)) = 0.7
        _HeightWeight  ("Gradient: Bottom to Top", Range(0, 1)) = 0.35
        _GradientPower ("Gradient Power", Range(0.2, 4)) = 1.2

        [Header(Variation)]
        _VariationColorA   ("Per-Plant Variation A (cool)", Color) = (0.30, 0.48, 0.24, 1)
        _VariationColorB   ("Per-Plant Variation B (warm)", Color) = (0.50, 0.50, 0.20, 1)
        _VariationStrength ("Per-Plant Variation Strength", Range(0, 1)) = 0.5
        _LeafBrightness    ("Per-Leaf Brightness Jitter", Range(0, 0.6)) = 0.25
        _LeafTintColor     ("Per-Leaf Warm Tint", Color) = (0.55, 0.40, 0.18, 1)
        _LeafTintAmount    ("Per-Leaf Warm Tint Amount", Range(0, 1)) = 0.35
        _ClumpBrightness   ("Per-Limb Clump Brightness", Range(0, 0.6)) = 0.2

        [Header(Accents)]
        _AccentColorA  ("Accent Colour A", Color) = (0.97, 0.66, 0.78, 1)
        _AccentColorB  ("Accent Colour B", Color) = (0.98, 0.92, 0.80, 1)
        _AccentChance  ("Accent Leaf Fraction", Range(0, 1)) = 0
        _AccentSize    ("Accent Size (fraction of leaf from tip)", Range(0, 1)) = 0.45
        _AccentEmission("Accent Glow", Range(0, 4)) = 0

        [Header(Bark Texture)]
        [Toggle(_BARK_TEXTURE)] _BarkTexture ("Bark Texture", Float) = 0
        [NoScaleOffset] _BarkMap ("Bark Map (R value, G groove, B height)", 2D) = "grey" {}
        _BarkTiling    ("Bark Tiles per Metre", Float) = 0.5
        _BarkStrength  ("Bark Texture Strength", Range(0, 1)) = 1
        _GrooveColor   ("Seam Colour (grooves, pores, veins)", Color) = (0.12, 0.09, 0.07, 1)
        _GrooveStrength("Seam Tint", Range(0, 1)) = 0.4
        _MossColor     ("Moss Colour", Color) = (0.30, 0.38, 0.15, 1)
        _MossAmount    ("Moss Amount", Range(0, 1)) = 0
        _MossHeight    ("Moss Reach Up the Trunk (height01)", Range(0.01, 1)) = 0.25

        [Header(Lighting)]
        _LightWrap       ("Light Wrap", Range(0, 1)) = 0.5
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1.0
        _InnerOcclusion  ("Inner Leaf Light", Range(0, 1)) = 0.45
        _ShadowStrength  ("Received Shadow Strength", Range(0, 1)) = 0.7
        _Translucency    ("Back-lit Leaf Glow", Range(0, 2)) = 0.5

        [Header(Wind)]
        _WindStrength     ("Lean (m per m of height)", Range(0, 0.5)) = 0.03
        _WaveLength       ("Wave Length (m)", Float) = 9
        _WaveSpeed        ("Wave Speed (m/s)", Float) = 1.8
        _WindFrequency    ("Gust Size (1/m)", Float) = 0.12
        _WindSpeed        ("Gust Speed (m/s)", Float) = 4
        _LimbSway         ("Limb Sway (m)", Range(0, 1)) = 0.12
        _LimbFrequency    ("Limb Sway Speed", Range(0, 6)) = 1.3
        _FlutterStrength  ("Leaf Flutter (m)", Range(0, 0.3)) = 0.05
        _FlutterFrequency ("Leaf Flutter Speed", Range(0, 20)) = 7
        _GustBrighten     ("Gust Brighten", Range(0, 1)) = 0.25

        [Header(Under Water)]
        _SurgeStrength    ("Surge: Rock to and fro (m per m of height)", Range(0, 0.3)) = 0
        _SurgePeriod      ("Surge Period (s)", Range(1, 20)) = 6
        _SurgeLength      ("Surge Wave Length (m)", Float) = 40

        [Header(Interaction)]
        _BendStrength ("Bender Push (m)", Range(0, 2)) = 0
        _BendRustle   ("Leaf Shake (m) as a body pushes through", Range(0, 0.3)) = 0

        [Header(Rendering)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (Off for leaves)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "StylizedFoliage.hlsl"
        ENDHLSL

        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local _BARK_TEXTURE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  albedo     : TEXCOORD1;
                half3  ambient    : TEXCOORD2;   // sky light
                half3  direct     : TEXCOORD3;   // sun light, before its shadow
                float3 accentFog  : TEXCOORD4;   // x accent hue (0 = none), y along the leaf, z fog
            #if defined(_BARK_TEXTURE)
                float3 barkPos    : TEXCOORD5;   // unswayed offset from the pivot, world-aligned, metres
                half4  barkFrame  : TEXCOORD6;   // xyz world normal, w height01
            #endif
            };

            Varyings vert(FoliageAttributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                FoliageVertex v = PlaceFoliageVertex(IN);
                Varyings OUT;
                OUT.positionWS = v.positionWS;
                OUT.positionCS = TransformWorldToHClip(v.positionWS);
                OUT.albedo = FoliageAlbedo(v);
                OUT.accentFog = float3(FoliageAccent(v), v.alongLeaf, ComputeFogFactor(OUT.positionCS.z));
            #if defined(_BARK_TEXTURE)
                // The models are imported at ×100 and Z-up, so object space is neither metres nor
                // upright: rotate and scale the rest position into world axes, but keep it
                // anchored at the pivot so the bark rides the tree instead of swimming over it.
                OUT.barkPos = mul((float3x3)GetObjectToWorldMatrix(), IN.positionOS.xyz);
                OUT.barkFrame = half4(v.normalWS, v.height01);
            #endif

                // The blob normal, wrapped, so a crown shades as soft round masses. Leaves deep
                // in the crown get less of the sky and the sun; outer ones glow when back-lit.
                Light sun = GetMainLight();
                half3 normal = normalize(v.normalWS);
                half wrapped = saturate((dot(normal, sun.direction) + _LightWrap) / (1.0 + _LightWrap));
                float3 toCamera = normalize(_WorldSpaceCameraPos - v.positionWS);
                half backlit = pow(saturate(dot(-toCamera, sun.direction)), 4.0) * v.shell * _Translucency;
                half occlusion = lerp(_InnerOcclusion, 1.0, v.shell);

                OUT.ambient = SampleSH(normal) * (_AmbientStrength * occlusion);
                OUT.direct = sun.color * (wrapped * occlusion + backlit);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(IN.positionCS);
            #endif
                Light sun = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                half shadow = lerp(1.0, sun.shadowAttenuation, _ShadowStrength);
                half3 glow;
                half3 albedo = FoliageAccentAlbedo(IN.albedo, IN.accentFog.x, IN.accentFog.y, glow);
            #if defined(_BARK_TEXTURE)
                albedo = BarkAlbedo(albedo, IN.barkPos, normalize(IN.barkFrame.xyz), IN.barkFrame.w);
            #endif
                half3 colour = albedo * (IN.ambient + IN.direct * shadow) + glow;
                return half4(MixFog(colour, IN.accentFog.z), 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 vert(FoliageAttributes IN) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                FoliageVertex v = PlaceFoliageVertex(IN);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirection = normalize(_LightPosition - v.positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(v.positionWS, normalize(v.normalWS), lightDirection));
                return ApplyShadowClamping(positionCS);
            }

            half4 frag(float4 positionCS : SV_POSITION) : SV_Target
            {
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(positionCS);
            #endif
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // The water reads the depth texture for its shore foam.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            float4 vert(FoliageAttributes IN) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                return TransformWorldToHClip(PlaceFoliageVertex(IN).positionWS);
            }

            half frag(float4 positionCS : SV_POSITION) : SV_Target
            {
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(positionCS);
            #endif
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // SSAO reads this; the blob normal keeps occlusion off the individual leaves.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "../DepthNormals.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            Varyings vert(FoliageAttributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                FoliageVertex v = PlaceFoliageVertex(IN);
                Varyings OUT;
                OUT.positionCS = TransformWorldToHClip(v.positionWS);
                OUT.normalWS = v.normalWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(IN.positionCS);
            #endif
                return EncodeDepthNormal(IN.normalWS);
            }
            ENDHLSL
        }
    }
}
