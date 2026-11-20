Shader "SpaceGame/StylizedTerrain"
{
    Properties
    {
        [Header(Palette Height Bands)]
        _ColorValley   ("Valley Low",   Color) = (0.32, 0.10, 0.06, 1.0)
        _ColorMid      ("Mid",          Color) = (0.62, 0.28, 0.14, 1.0)
        _ColorHigh     ("High",         Color) = (0.82, 0.52, 0.30, 1.0)
        _ColorPeak     ("Peak Dust",    Color) = (0.92, 0.78, 0.58, 1.0)
        _ColorCliff    ("Cliff Steep",  Color) = (0.22, 0.10, 0.07, 1.0)

        [Header(Height Bands)]
        _MinHeight     ("Min World Height", Float) = 0
        _MaxHeight     ("Max World Height", Float) = 200
        _BandSoftness  ("Band Softness", Range(0.001, 0.5)) = 0.08
        _BandNoise     ("Band Noise Amount", Range(0, 1)) = 0.35
        _BandNoiseScale("Band Noise Scale", Float) = 18

        [Header(Slope)]
        _SlopeStart    ("Slope Start", Range(0, 1)) = 0.55
        _SlopeEnd      ("Slope End",   Range(0, 1)) = 0.80

        [Header(Cliff Strata)]
        _ColorStrata   ("Strata Layer", Color) = (0.40, 0.20, 0.14, 1.0)
        _StrataStrength("Strata Strength", Range(0, 1)) = 0
        _StrataSpacing ("Strata Layer Height (m)", Float) = 6
        _StrataEdge    ("Strata Edge Softness", Range(0.01, 1)) = 0.3
        _StrataWarp    ("Strata Warp (m)", Float) = 8
        _StrataWarpScale("Strata Warp Scale (m)", Float) = 120

        [Header(Macro Patches)]
        _ColorMacro    ("Patch Colour", Color) = (0.90, 0.75, 0.60, 1.0)
        _MacroStrength ("Patch Strength", Range(0, 1)) = 0
        _MacroScale    ("Patch Scale (m)", Float) = 400
        _MacroCoverage ("Patch Coverage", Range(0, 1)) = 0.4

        // Painterly variation on top of the height palette. Every strength defaults to 0, so a
        // material that does not set them renders as before.
        [Header(Painted Patches)]
        _PaintStrength    ("Paint Strength (0 = off)", Range(0, 2)) = 0
        _ColorPaintDark   ("Darker Patches", Color) = (0.776, 0.451, 0.247, 1.0)
        _ColorPaintLight  ("Paler Patches",  Color) = (0.922, 0.635, 0.396, 1.0)
        _PaintRegionSize  ("Region Size (m)", Float) = 140
        _PaintPatchSize   ("Patch Size (m)",  Float) = 22
        _PaintDabSize     ("Dab Size (m)",    Float) = 4.5
        _PaintDabStrength ("Dab Strength", Range(0, 2)) = 0.8
        _ColorStreak      ("Wind Streaks", Color) = (0.890, 0.588, 0.357, 1.0)
        _StreakStrength   ("Streak Strength (0 = off)", Range(0, 1)) = 0
        _ColorPebble      ("Pebbles", Color) = (0.718, 0.416, 0.251, 1.0)
        _PebbleDensity    ("Pebble Density (0 = off)", Range(0, 0.3)) = 0
        _RippleStrength   ("Ripple Strength (0 = off)", Range(0, 2)) = 0

        // Two vegetation grounds placed by a large, warped, many-octave noise, with the colours
        // above (the material's own ground) on high and steep land. Blending the palettes, not
        // finished colours, keeps patches and streaks continuous across a border. 0 = off.
        [Header(Biomes)]
        _BiomeStrength    ("Biome Strength (0 = off)", Range(0, 1)) = 0
        _BiomeScale       ("Biome Size (m)", Float) = 550
        _BiomeOctaves     ("Biome Octaves", Range(1, 8)) = 7
        _BiomeBlend       ("Transition Width", Range(0.01, 0.5)) = 0.24
        _BiomeWarp        ("Warp", Range(0, 2)) = 0.9
        _BiomeTerrainPull ("Terrain Influence (forest to valleys)", Range(0, 1)) = 0.5
        _BiomeHighGround  ("Own Ground On Heights", Range(0, 1)) = 1

        [Header(Biome A Dry Grassland)]
        _DryBase          ("Base",           Color) = (0.710, 0.647, 0.369, 1.0)
        _DryDark          ("Darker Patches", Color) = (0.612, 0.553, 0.298, 1.0)
        _DryLight         ("Paler Patches",  Color) = (0.796, 0.737, 0.455, 1.0)
        _DryStreak        ("Wind Streaks",   Color) = (0.761, 0.698, 0.416, 1.0)
        _DryPebble        ("Pebbles",        Color) = (0.788, 0.706, 0.557, 1.0)
        _DryCliff         ("Cliff",          Color) = (0.627, 0.471, 0.314, 1.0)
        _DryStrata        ("Cliff Strata",   Color) = (0.725, 0.557, 0.369, 1.0)
        _DryShadow        ("Shadow Tint",    Color) = (0.12, 0.10, 0.20, 1.0)
        _DryDetail        ("Ripples, Pebbles, Dabs, Streaks", Vector) = (0.3, 0.025, 0.9, 0.55)

        [Header(Biome B Forest Floor)]
        _ForestBase       ("Base",           Color) = (0.369, 0.420, 0.243, 1.0)
        _ForestDark       ("Darker Patches", Color) = (0.290, 0.337, 0.196, 1.0)
        _ForestLight      ("Paler Patches",  Color) = (0.447, 0.498, 0.290, 1.0)
        _ForestStreak     ("Wind Streaks",   Color) = (0.400, 0.443, 0.247, 1.0)
        _ForestPebble     ("Pebbles",        Color) = (0.541, 0.494, 0.400, 1.0)
        _ForestCliff      ("Cliff",          Color) = (0.431, 0.369, 0.282, 1.0)
        _ForestStrata     ("Cliff Strata",   Color) = (0.522, 0.447, 0.345, 1.0)
        _ForestShadow     ("Shadow Tint",    Color) = (0.09, 0.09, 0.17, 1.0)
        _ForestDetail     ("Ripples, Pebbles, Dabs, Streaks", Vector) = (0, 0.04, 1.1, 0.15)

        // Painted ground layers override the biomes where painted. TerrainGroundPaint binds, through
        // the terrain's splat property block, up to four of the terrain's splat textures and, per
        // channel, which ground it paints: 0 none, 1 dry grassland, 2 forest floor, 3 the
        // material's own ground, 4 + n row n of the palette table (PaintedGroundPalette assets).
        [Header(Painted Ground)]
        _PaintMapWarp     ("Painted Edge Warp (m)", Float) = 8
        [HideInInspector][NoScaleOffset] _PaintSplat0 ("Painted Splat 0", 2D) = "black" {}
        [HideInInspector][NoScaleOffset] _PaintSplat1 ("Painted Splat 1", 2D) = "black" {}
        [HideInInspector][NoScaleOffset] _PaintSplat2 ("Painted Splat 2", 2D) = "black" {}
        [HideInInspector][NoScaleOffset] _PaintSplat3 ("Painted Splat 3", 2D) = "black" {}
        [HideInInspector] _PaintSplatGround0 ("Splat 0 Grounds", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PaintSplatGround1 ("Splat 1 Grounds", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PaintSplatGround2 ("Splat 2 Grounds", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PaintSplatGround3 ("Splat 3 Grounds", Vector) = (0, 0, 0, 0)
        [HideInInspector][NoScaleOffset] _PaintPalettes ("Palette Table", 2D) = "black" {}
        [HideInInspector][NoScaleOffset] _PaintPatterns ("Pattern Textures", 2DArray) = "" {}
        [HideInInspector] _PaintMapRect    ("Painted Map Rect (origin xz, 1/size xz)", Vector) = (0, 0, 0, 0)

        // One tiling map read near the camera, faded out with distance so it never shows its
        // tiling. Gravel takes the ground palette's pebble colour. Strengths default to 0 = off.
        [Header(Ground Detail)]
        [NoScaleOffset] _DetailMap ("Detail Map (R gravel tone, G cracks, B brush, A gravel height)", 2D) = "black" {}
        _DetailTile    ("Detail Tile (m)", Float) = 4
        _DetailFadeStart("Detail Fade Start (m)", Float) = 12
        _DetailFadeEnd ("Detail Fade End (m)", Float) = 45
        _GravelStrength("Gravel Strength", Range(0, 1)) = 0
        _GravelClumpSize("Gravel Clump Size (m)", Float) = 10
        _GravelCoverage("Gravel Coverage (cracks fill the rest)", Range(0, 1)) = 1
        _CrackStrength ("Crack Darkening", Range(0, 1)) = 0
        _BrushStrength ("Brush Stroke Strength", Range(0, 0.5)) = 0

        [Header(Lighting)]
        [Toggle(_FLAT_SHADING)] _FlatShading ("Flat Shading", Float) = 1
        _AmbientBoost  ("Ambient Boost", Range(0, 1)) = 0.25
        _ShadowTint    ("Shadow Tint (added to ambient)", Color) = (0, 0, 0, 1)
        _LitTint       ("Lit Tint (multiplies the sun)", Color) = (1, 1, 1, 1)
        _LightWrap     ("Light Wrap", Range(0, 1)) = 0.35
        _LightSteps    ("Toon Light Steps (0 = smooth)", Range(0, 4)) = 0
        _LightStepSoftness("Toon Step Softness", Range(0.01, 0.5)) = 0.12
        _GrassRootShade("Ground Under Grass, Near (x grass colour)", Range(0, 1)) = 1

        [Header(Atmosphere)]
        _RimColor      ("Rim Color", Color) = (1.0, 0.55, 0.30, 1.0)
        _RimPower      ("Rim Power", Range(0.5, 8)) = 3.0
        _RimStrength   ("Rim Strength", Range(0, 2)) = 0.4
        _FogTint       ("Distance Tint", Color) = (0.85, 0.55, 0.40, 1.0)
        _FogStart      ("Distance Tint Start", Float) = 80
        _FogEnd        ("Distance Tint End",   Float) = 600
        _FogStrength   ("Distance Tint Strength", Range(0, 1)) = 0.0

        // Set per terrain by GrassField (GrassGroundTint) through the splat property block, so
        // painted grass reads as colour on the ground from afar. A zero rect means none.
        [HideInInspector][NoScaleOffset] _GrassColorMap ("Grass Colour Map", 2D) = "black" {}
        [HideInInspector] _GrassMapRect   ("Grass Map Rect (origin xz, 1/size xz)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _GrassMinHeight ("Grass Min Height", Float) = -10000
        [HideInInspector] _GrassMapWarp   ("Grass Edge Warp (metres, period)", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType"       = "Opaque"
            "RenderPipeline"   = "UniversalPipeline"
            "TerrainCompatible"= "True"
            "Queue"            = "Geometry"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            // 3.5: the painted grounds' pattern textures are a texture array.
            #pragma target 3.5

            #pragma shader_feature_local _FLAT_SHADING

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "../Effects/Flashlight.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorValley;
                float4 _ColorMid;
                float4 _ColorHigh;
                float4 _ColorPeak;
                float4 _ColorCliff;
                float  _MinHeight;
                float  _MaxHeight;
                float  _BandSoftness;
                float  _BandNoise;
                float  _BandNoiseScale;
                float  _SlopeStart;
                float  _SlopeEnd;
                float4 _ColorStrata;
                float  _StrataStrength;
                float  _StrataSpacing;
                float  _StrataEdge;
                float  _StrataWarp;
                float  _StrataWarpScale;
                float4 _ColorMacro;
                float  _MacroStrength;
                float  _MacroScale;
                float  _MacroCoverage;
                float  _PaintStrength;
                float4 _ColorPaintDark;
                float4 _ColorPaintLight;
                float  _PaintRegionSize;
                float  _PaintPatchSize;
                float  _PaintDabSize;
                float  _PaintDabStrength;
                float4 _ColorStreak;
                float  _StreakStrength;
                float4 _ColorPebble;
                float  _PebbleDensity;
                float  _RippleStrength;
                float  _BiomeStrength;
                float  _BiomeScale;
                float  _BiomeOctaves;
                float  _BiomeBlend;
                float  _BiomeWarp;
                float  _BiomeTerrainPull;
                float  _BiomeHighGround;
                float4 _DryBase;
                float4 _DryDark;
                float4 _DryLight;
                float4 _DryStreak;
                float4 _DryPebble;
                float4 _DryCliff;
                float4 _DryStrata;
                float4 _DryShadow;
                float4 _DryDetail;
                float4 _ForestBase;
                float4 _ForestDark;
                float4 _ForestLight;
                float4 _ForestStreak;
                float4 _ForestPebble;
                float4 _ForestCliff;
                float4 _ForestStrata;
                float4 _ForestShadow;
                float4 _ForestDetail;
                float  _PaintMapWarp;
                float4 _PaintSplatGround0;
                float4 _PaintSplatGround1;
                float4 _PaintSplatGround2;
                float4 _PaintSplatGround3;
                float4 _PaintMapRect;
                float  _DetailTile;
                float  _DetailFadeStart;
                float  _DetailFadeEnd;
                float  _GravelStrength;
                float  _GravelClumpSize;
                float  _GravelCoverage;
                float  _CrackStrength;
                float  _BrushStrength;
                float  _AmbientBoost;
                float4 _ShadowTint;
                float4 _LitTint;
                float  _LightWrap;
                float  _LightSteps;
                float  _LightStepSoftness;
                float  _GrassRootShade;
                float4 _RimColor;
                float  _RimPower;
                float  _RimStrength;
                float4 _FogTint;
                float  _FogStart;
                float  _FogEnd;
                float  _FogStrength;
                float4 _GrassMapRect;
                float  _GrassMinHeight;
                float4 _GrassMapWarp;
            CBUFFER_END

            TEXTURE2D(_GrassColorMap);
            SAMPLER(sampler_GrassColorMap);
            TEXTURE2D(_DetailMap);
            SAMPLER(sampler_DetailMap);
            // The terrain's own splat textures that hold painted ground layers, and the palettes of
            // the PaintedGroundPalette grounds, one row each (TerrainGroundPaint).
            TEXTURE2D(_PaintSplat0);
            TEXTURE2D(_PaintSplat1);
            TEXTURE2D(_PaintSplat2);
            TEXTURE2D(_PaintSplat3);
            TEXTURE2D(_PaintPalettes);
            SAMPLER(sampler_linear_clamp);
            // The palette grounds' stylized pattern textures, one slice each (TerrainGroundPaint).
            TEXTURE2D_ARRAY(_PaintPatterns);
            SAMPLER(sampler_PaintPatterns);
            // Set globally by GrassField: x the distance the grass starts hiding the ground, y one
            // over the length of that ramp, z the extra tuft layers a far view looks through.
            float4 _GrassGroundCover;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // 2D value-noise — cheap, smooth, good for jittering band edges.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Two rotated octaves, so large patches do not show the value-noise grid.
            float patchNoise(float2 p)
            {
                float2 q = float2(p.x * 0.8 - p.y * 0.6, p.x * 0.6 + p.y * 0.8) * 2.03 + 17.0;
                return valueNoise(p) * 0.67 + valueNoise(q) * 0.33;
            }

            // Horizontal sediment layers: each layer gets its own 0..1 tone, blended across a
            // soft edge so distant cliffs do not alias. The layer height is warped by noise so
            // the bands follow the hillside instead of reading as ruled lines.
            float strataTone(float3 positionWS)
            {
                float warp = (valueNoise(positionWS.xz / max(0.0001, _StrataWarpScale)) - 0.5) * _StrataWarp;
                float layer = (positionWS.y + warp) / max(0.0001, _StrataSpacing);
                float index = floor(layer);
                float blend = smoothstep(1.0 - _StrataEdge, 1.0, frac(layer));
                return lerp(hash21(float2(index, 7.0)), hash21(float2(index + 1.0, 7.0)), blend);
            }

            // ---- Painted patches ----
            // Their own hash: hash21 multiplies by ~456 before frac, which loses all precision at
            // the cell counts these reach on an 8 km terrain (pebble cells, brush edges).
            float paintHash(float2 p)
            {
                float3 q = frac(float3(p.xyx) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }
            float paintNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(paintHash(i), paintHash(i + float2(1, 0)), u.x),
                            lerp(paintHash(i + float2(0, 1)), paintHash(i + float2(1, 1)), u.x), u.y);
            }
            // 1 while a feature of this size spans several pixels, 0 once it is sub-pixel.
            float fadeBySize(float size, float px)
            {
                return 1.0 - smoothstep(size * 0.05, size * 0.22, px);
            }
            // Warped blob noise cut at a threshold: flat inside, with a soft but readable border
            // roughened like a brush edge. cover 0..1 is roughly how much ground it takes.
            float paintPatches(float2 p, float size, float cover, float soft, float seed, float px)
            {
                float2 w = (float2(paintNoise(p / (size * 1.6) + seed), paintNoise(p / (size * 1.6) + seed + 5.0)) - 0.5) * size * 1.2;
                float2 q = (p + w) / size + seed * 7.0;
                float n = paintNoise(q) * 0.65 + paintNoise(q * 2.1 + 3.0) * 0.25 + paintNoise(q * 4.3) * 0.1;
                n += (paintNoise(p / (size * 0.08) + seed) - 0.5) * 0.05 * fadeBySize(size * 0.08, px);
                float t = 0.7 - cover * 0.4;
                return smoothstep(t - soft, t + soft, n);
            }
            // Everything a ground needs to be painted: one base colour, its patch, streak, pebble,
            // cliff and shadow colours, detail = (ripples, pebble density, dabs, streaks) and
            // soil = (gravel, crack darkening, pebble tone variation, gravel coverage). A ground's
            // shape (its scales) is kept apart, in GroundShapes: scales must not be blended.
            struct GroundPalette
            {
                float3 base;
                float3 dark;
                float3 light;
                float3 streak;
                float3 pebble;
                float3 cliff;
                float3 strata;
                float3 shadow;
                float4 detail;
                float4 soil;
            };
            GroundPalette lerpPalette(GroundPalette a, GroundPalette b, float w)
            {
                GroundPalette r;
                r.base   = lerp(a.base,   b.base,   w);
                r.dark   = lerp(a.dark,   b.dark,   w);
                r.light  = lerp(a.light,  b.light,  w);
                r.streak = lerp(a.streak, b.streak, w);
                r.pebble = lerp(a.pebble, b.pebble, w);
                r.cliff  = lerp(a.cliff,  b.cliff,  w);
                r.strata = lerp(a.strata, b.strata, w);
                r.shadow = lerp(a.shadow, b.shadow, w);
                r.detail = lerp(a.detail, b.detail, w);
                r.soil   = lerp(a.soil,   b.soil,   w);
                return r;
            }
            // The material grounds' soil and shape: the near detail and scales they had before
            // grounds carried their own. shape = (patch scale, pebble spacing m, pebble radius /
            // spacing, ripple scale).
            float4 materialSoil()  { return float4(1.0, 1.0, 0.0, _GravelCoverage); }
            float4 materialShape() { return float4(1.0, 1.4, 0.0929, 1.0); }
            // pattern = (texture slice, -1 none; tile m; strength; 1 = may rotate). The material's
            // grounds are fully procedural.
            float4 materialPattern() { return float4(-1.0, 1.0, 0.0, 0.0); }
            // The material's own ground: the height ramp as base, plus the painted-patch colours.
            GroundPalette ownPalette(float3 heightColour)
            {
                GroundPalette r;
                r.base = heightColour;
                r.dark = _ColorPaintDark.rgb;
                r.light = _ColorPaintLight.rgb;
                r.streak = _ColorStreak.rgb;
                r.pebble = _ColorPebble.rgb;
                r.cliff = _ColorCliff.rgb;
                r.strata = _ColorStrata.rgb;
                r.shadow = _ShadowTint.rgb;
                r.detail = float4(_RippleStrength, _PebbleDensity, _PaintDabStrength, _StreakStrength);
                r.soil = materialSoil();
                return r;
            }
            GroundPalette dryPalette()
            {
                GroundPalette r;
                r.base = _DryBase.rgb; r.dark = _DryDark.rgb; r.light = _DryLight.rgb; r.streak = _DryStreak.rgb;
                r.pebble = _DryPebble.rgb; r.cliff = _DryCliff.rgb; r.strata = _DryStrata.rgb; r.shadow = _DryShadow.rgb;
                r.detail = _DryDetail;
                r.soil = materialSoil();
                return r;
            }
            GroundPalette forestPalette()
            {
                GroundPalette r;
                r.base = _ForestBase.rgb; r.dark = _ForestDark.rgb; r.light = _ForestLight.rgb; r.streak = _ForestStreak.rgb;
                r.pebble = _ForestPebble.rgb; r.cliff = _ForestCliff.rgb; r.strata = _ForestStrata.rgb; r.shadow = _ForestShadow.rgb;
                r.detail = _ForestDetail;
                r.soil = materialSoil();
                return r;
            }
            // Row n of the palette table, in GroundPalette field order (PaintedGroundPalette.Write).
            GroundPalette tablePalette(int row)
            {
                GroundPalette r;
                r.base   = LOAD_TEXTURE2D(_PaintPalettes, int2(0,  row)).rgb;
                r.dark   = LOAD_TEXTURE2D(_PaintPalettes, int2(1,  row)).rgb;
                r.light  = LOAD_TEXTURE2D(_PaintPalettes, int2(2,  row)).rgb;
                r.streak = LOAD_TEXTURE2D(_PaintPalettes, int2(3,  row)).rgb;
                r.pebble = LOAD_TEXTURE2D(_PaintPalettes, int2(4,  row)).rgb;
                r.cliff  = LOAD_TEXTURE2D(_PaintPalettes, int2(5,  row)).rgb;
                r.strata = LOAD_TEXTURE2D(_PaintPalettes, int2(6,  row)).rgb;
                r.shadow = LOAD_TEXTURE2D(_PaintPalettes, int2(7,  row)).rgb;
                r.detail = LOAD_TEXTURE2D(_PaintPalettes, int2(8,  row));
                r.soil   = LOAD_TEXTURE2D(_PaintPalettes, int2(9,  row));
                return r;
            }
            float4 tableShape(int row)   { return LOAD_TEXTURE2D(_PaintPalettes, int2(10, row)); }
            float4 tablePattern(int row) { return LOAD_TEXTURE2D(_PaintPalettes, int2(11, row)); }

            // The two strongest shapes (with their pattern textures) under a pixel. A blended scale
            // would sweep p / scale through hundreds of cells across one border far from the origin,
            // and two textures cannot be blended into one, so each is drawn and crossfaded instead.
            struct GroundShapes
            {
                float4 a;
                float4 b;
                float4 pa;
                float4 pb;
                float  wa;
                float  wb;
            };
            GroundShapes singleShape(float4 shape, float4 pattern)
            {
                GroundShapes r;
                r.a = shape; r.b = shape; r.pa = pattern; r.pb = pattern; r.wa = 1.0; r.wb = 0.0;
                return r;
            }
            void addShape(inout GroundShapes s, float4 shape, float4 pattern, float w)
            {
                if (all(shape == s.a) && all(pattern == s.pa))      s.wa += w;
                else if (all(shape == s.b) && all(pattern == s.pb)) s.wb += w;
                else if (w > s.wa) { s.b = s.a; s.pb = s.pa; s.wb = s.wa; s.a = shape; s.pa = pattern; s.wa = w; }
                else if (w > s.wb) { s.b = shape; s.pb = pattern; s.wb = w; }
                if (s.wb > s.wa)
                {
                    float4 t = s.a; s.a = s.b; s.b = t;
                    t = s.pa; s.pa = s.pb; s.pb = t;
                    float tw = s.wa; s.wa = s.wb; s.wb = tw;
                }
            }
            // How much of the second shape to draw.
            float secondShare(GroundShapes s)
            {
                return s.wb / max(1e-4, s.wa + s.wb);
            }

            // fBm with up to eight rotated octaves; fractional octave counts fade the last one in.
            float biomeNoise(float2 p, float octaves)
            {
                float s = 0.0, a = 0.5, n = 0.0;
                [loop] for (int i = 0; i < 8; i++)
                {
                    float w = saturate(octaves - i);
                    if (w <= 0.0) break;
                    s += a * w * paintNoise(p);
                    n += a * w;
                    p = float2(p.x * 0.8 - p.y * 0.6, p.x * 0.6 + p.y * 0.8) * 2.03 + 17.0;
                    a *= 0.5;
                }
                return s / n;
            }
            // 0 = dry grassland, 1 = forest floor. Low octaves give the big shapes, high ones a
            // fractal edge; the terrain nudges forest into valleys and onto flats.
            float forestAmount(float2 xz, float heightFraction, float slope)
            {
                float2 q = xz / max(1.0, _BiomeScale);
                q += (float2(biomeNoise(q * 0.7 + 3.1, 5.0), biomeNoise(q * 0.7 + 8.7, 5.0)) - 0.5) * _BiomeWarp;
                float t = (biomeNoise(q, _BiomeOctaves) - 0.5) * 2.4 + 0.5;   // an octave sum bunches near 0.5: spread it
                t += _BiomeTerrainPull * ((0.5 - heightFraction) * 0.6 - slope * 0.8);
                return smoothstep(0.5 - _BiomeBlend, 0.5 + _BiomeBlend, t);
            }
            // How much of the material's own ground takes the high and steep land.
            float highGroundAmount(float2 xz, float heightFraction, float slope)
            {
                float t = heightFraction * 1.3 + slope * 0.9
                        + (biomeNoise(xz / max(1.0, _BiomeScale * 0.6) + 21.0, _BiomeOctaves) - 0.5) * 0.9;
                return smoothstep(0.78 - _BiomeBlend, 0.78 + _BiomeBlend, t) * _BiomeHighGround;
            }
            // The splat UV, edges warped so the coarse splat texels do not show.
            float2 paintedUV(float2 xz)
            {
                float2 warp = (float2(paintNoise(xz / 40.0), paintNoise(xz / 40.0 + 7.3)) - 0.5) * 2.0 * _PaintMapWarp;
                return (xz + warp - _PaintMapRect.xy) * _PaintMapRect.zw;
            }
            GroundPalette paletteByCode(float code, float3 heightColour)
            {
                if (code < 1.5) return dryPalette();
                if (code < 2.5) return forestPalette();
                if (code < 3.5) return ownPalette(heightColour);
                return tablePalette((int)(code - 3.5));
            }
            float4 shapeByCode(float code)
            {
                return code < 3.5 ? materialShape() : tableShape((int)(code - 3.5));
            }
            float4 patternByCode(float code)
            {
                return code < 3.5 ? materialPattern() : tablePattern((int)(code - 3.5));
            }
            // Folds one splat texture's painted grounds into a running weighted average.
            void addPainted(inout GroundPalette painted, inout GroundShapes shapes, inout float total,
                            float4 weights, float4 codes, float3 heightColour)
            {
                [unroll] for (int c = 0; c < 4; c++)
                {
                    float w = codes[c] > 0.5 ? weights[c] : 0.0;
                    [branch] if (w > 0.001)
                    {
                        total += w;
                        painted = lerpPalette(painted, paletteByCode(codes[c], heightColour), w / total);
                        addShape(shapes, shapeByCode(codes[c]), patternByCode(codes[c]), w);
                    }
                }
            }

            // Regions, patches and dabs a shade darker or paler than the palette's base, all at
            // patch scale k.
            float3 applyPaint(GroundPalette g, float2 p, float px, float k)
            {
                float v = _PaintStrength;
                float3 c = g.base;
                c = lerp(c, g.dark,  v * 0.45 * paintPatches(p, _PaintRegionSize * k,        0.45, 0.16,  1.0, px));
                c = lerp(c, g.light, v * 0.40 * paintPatches(p, _PaintRegionSize * k * 0.8,  0.40, 0.16,  2.0, px));
                c = lerp(c, g.dark,  v * 0.65 * paintPatches(p, _PaintPatchSize * k,         0.35, 0.025, 3.0, px));
                c = lerp(c, g.light, v * 0.55 * paintPatches(p, _PaintPatchSize * k * 0.73, 0.30, 0.025, 4.0, px));
                float dab = v * g.detail.z * fadeBySize(_PaintDabSize * k, px);
                c = lerp(c, g.dark,  dab * 0.50 * paintPatches(p, _PaintDabSize * k,        0.28, 0.02, 5.0, px));
                c = lerp(c, g.light, dab * 0.45 * paintPatches(p, _PaintDabSize * k * 0.71, 0.22, 0.02, 6.0, px));
                return c;
            }
            // Long, thin streaks along a wind direction that turns slowly over ~200 m.
            float windStreak(float2 p)
            {
                float a = 0.5 + (paintNoise(p / 200.0) - 0.5) * 0.8;
                float2 r = float2(p.x * cos(a) - p.y * sin(a), p.x * sin(a) + p.y * cos(a));
                float s = paintNoise(r * float2(1.0 / 30.0, 1.0 / 2.5)) * 0.7 + paintNoise(r * float2(1.0 / 12.0, 1.0 / 1.2)) * 0.3;
                return smoothstep(0.58, 0.74, s);
            }
            // A rare pebble, painted rather than lit (lit ones read as holes from a low angle):
            // returns 1 inside it, shade 0 on its lit top to 1 underneath, the soft shadow it
            // drops to one side, and a 0..1 tone of its own. One pebble per cell of the given
            // spacing at most, its radius up to maxRadius × spacing.
            float paintPebble(float2 p, float density, float cell, float maxRadius, out float shade, out float dropShadow, out float tone)
            {
                float2 i = floor(p / cell);
                float2 f = frac(p / cell);
                float best = 8.0;
                float2 id = i;
                float2 off = 0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 r = g + 0.5 + (float2(paintHash(i + g), paintHash(i + g + 19.19)) - 0.5) * 0.8 - f;
                    float d = length(r);
                    if (d < best) { best = d; id = i + g; off = r; }
                }
                shade = 0.0;
                dropShadow = 0.0;
                tone = paintHash(id + 13.7);
                if (paintHash(id + 5.3) > density) return 0.0;
                float radius = cell * maxRadius * (0.385 + 0.615 * paintHash(id + 9.1));
                float2 o = -off * cell / radius;
                const float2 lightSide = float2(0.6, 0.8);
                shade = smoothstep(-0.4, 0.9, dot(o, lightSide));
                dropShadow = (1.0 - smoothstep(0.9, 1.5, length(o - lightSide * 0.55))) * step(1.0, length(o));
                return step(length(o), 1.0);
            }
            // Two sets of soft wind ripples (2.2 m and 3.6 m), bent by a warp, present in patches.
            float paintRipples(float2 p)
            {
                float2 w = float2(paintNoise(p / 23.0), paintNoise(p / 23.0 + 9.0)) * 7.0;
                float t1 = frac(dot(p + w, float2(0.8, 0.6)) / 2.2);
                float t2 = frac(dot(p + w * 1.3, float2(0.5, 0.87)) / 3.6);
                float r1 = smoothstep(0.0, 0.7, t1) * (1.0 - smoothstep(0.7, 1.0, t1));
                float r2 = smoothstep(0.0, 0.7, t2) * (1.0 - smoothstep(0.7, 1.0, t2));
                return lerp(r1, r2, smoothstep(0.35, 0.65, paintNoise(p / 80.0 + 2.0)))
                     * 0.05 * smoothstep(0.3, 0.6, paintNoise(p / 45.0 + 8.0));
            }

            // One variant of the tiling: the pattern shifted (and, where allowed, turned) by an amount
            // picked from the variant index. Gradients are passed in, so this is safe in branches.
            float3 patternVariant(float slice, float2 uv, float2 dx, float2 dy, float index, float rotate)
            {
                float2 offset = sin(float2(3.0, 7.0) * index);
                float angle = rotate * paintHash(float2(index, 2.7)) * 6.2832;
                float2x2 r = float2x2(cos(angle), -sin(angle), sin(angle), cos(angle));
                return SAMPLE_TEXTURE2D_ARRAY_GRAD(_PaintPatterns, sampler_PaintPatterns, mul(r, uv) + offset, slice,
                                                   mul(r, dx), mul(r, dy)).rgb;
            }
            // The pattern at tile size pattern.y, with no visible repeat: a world noise of about a tile
            // picks one of eight shifted variants per area, and neighbouring areas crossfade, the cut
            // following the pattern's own light and dark (Quilez's no-tile texturing).
            float3 samplePattern(float4 pattern, float2 p, float2 dpdx, float2 dpdy)
            {
                float2 uv = p / pattern.y;
                float2 dx = dpdx / pattern.y, dy = dpdy / pattern.y;
                float k = paintNoise(p / (pattern.y * 1.1)) * 0.7 + paintNoise(p / (pattern.y * 0.45) + 3.3) * 0.3;
                float l = k * 8.0;
                float index = floor(l);
                float3 a = patternVariant(pattern.x, uv, dx, dy, index, pattern.w);
                float3 b = patternVariant(pattern.x, uv, dx, dy, index + 1.0, pattern.w);
                float3 d = a - b;
                return lerp(a, b, smoothstep(0.2, 0.8, frac(l) - 0.1 * (d.x + d.y + d.z)));
            }

            // A palette's patches, wind streaks and pebbles at one shape, over its pattern texture if
            // it has one: the patches then shade the texture rather than replace it.
            float3 paintSurface(GroundPalette g, float4 shape, float4 pattern, float2 p, float px, float2 dpdx, float2 dpdy)
            {
                float3 c = g.base;
                [branch] if (_PaintStrength > 0.0)
                    c = applyPaint(g, p, px, shape.x);
                [branch] if (pattern.x >= 0.0)
                    c = lerp(c, samplePattern(pattern, p, dpdx, dpdy) * c / max(g.base, 1e-3), pattern.z);
                [branch] if (g.detail.w > 0.0)
                    c = lerp(c, g.streak, g.detail.w * windStreak(p) * fadeBySize(2.5, px));
                [branch] if (g.detail.y > 0.0)
                {
                    float pebbleFade = fadeBySize(shape.y * shape.z * 1.5, px);
                    float shade, dropShadow, tone;
                    float pebble = paintPebble(p, g.detail.y, shape.y, shape.z, shade, dropShadow, tone) * pebbleFade;
                    c *= 1.0 - 0.22 * dropShadow * pebbleFade;
                    // Tone variation spreads the stones between the pebble colour and a darker one.
                    float3 stone = lerp(g.pebble, g.dark * 0.75, tone * g.soil.z);
                    c = lerp(c, stone * lerp(1.18, 0.72, shade), pebble);
                }
                return c;
            }
            // Slope of the ripples at ripple scale k, by finite differences, gone once sub-pixel.
            float2 rippleSlope(float2 p, float px, float k)
            {
                float e = max(px * 0.6, 0.02);
                float r0 = paintRipples(p / k);
                float2 grad = float2(paintRipples((p + float2(e, 0)) / k) - r0, paintRipples((p + float2(0, e)) / k) - r0) / e;
                return grad * fadeBySize(2.2 * k, px);
            }

            // Smooth 3-stop blend used for the height palette.
            float3 blend3(float3 a, float3 b, float3 c, float t, float soft)
            {
                float w0 = 1.0 - smoothstep(0.5 - soft, 0.5 + soft, t);
                float wMidLow  = smoothstep(0.0 - soft, 0.0 + soft, t) * w0;
                float wMidHigh = smoothstep(0.5 - soft, 0.5 + soft, t);
                float3 lowMid = lerp(a, b, smoothstep(0.0, 0.5 + soft, t));
                return lerp(lowMid, c, smoothstep(0.5 - soft, 1.0, t));
            }

            // Cel bands: the light quantised into _LightSteps flat tones with soft edges between
            // them. Below one step the light stays smooth.
            float ToonStep(float light)
            {
                if (_LightSteps < 1.0)
                    return light;
                float steps = floor(_LightSteps);
                float scaled = light * steps;
                float edge = smoothstep(0.5 - _LightStepSoftness, 0.5 + _LightStepSoftness, frac(scaled));
                return (floor(scaled) + edge) / steps;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS   = vni.normalWS;
                OUT.fogFactor  = ComputeFogFactor(vpi.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

            #if defined(_FLAT_SHADING)
                float3 dpx = ddx(IN.positionWS);
                float3 dpy = ddy(IN.positionWS);
                float3 N   = normalize(cross(dpy, dpx));
            #else
                float3 N = normalize(IN.normalWS);
            #endif
                float dist = length(GetWorldSpaceViewDir(IN.positionWS));

                // ---- Height palette (with noise-jittered band thresholds) ----
                float jitter = (valueNoise(IN.positionWS.xz / max(0.0001, _BandNoiseScale)) - 0.5)
                               * _BandNoise * (_MaxHeight - _MinHeight);
                float h = saturate((IN.positionWS.y + jitter - _MinHeight)
                                   / max(0.0001, (_MaxHeight - _MinHeight)));

                // 4-stop height ramp: valley -> mid -> high -> peak.
                float soft = _BandSoftness;
                float3 cVM = lerp(_ColorValley.rgb, _ColorMid.rgb,
                                  smoothstep(0.20 - soft, 0.20 + soft, h));
                float3 cMH = lerp(cVM,              _ColorHigh.rgb,
                                  smoothstep(0.55 - soft, 0.55 + soft, h));
                float3 cHP = lerp(cMH,              _ColorPeak.rgb,
                                  smoothstep(0.85 - soft, 0.85 + soft, h));
                float3 albedo = cHP;

                // ---- Macro patches (dust drifts / darker regolith) ----
                float patch = smoothstep(1.0 - _MacroCoverage, 1.0 - _MacroCoverage + 0.2,
                                         patchNoise(IN.positionWS.xz / max(0.0001, _MacroScale)));
                albedo = lerp(albedo, _ColorMacro.rgb, patch * _MacroStrength);

                // ---- Ground palette: the material's own ground, the biomes, then painted layers ----
                // dot(N, up): 1 = flat ground, 0 = vertical cliff.
                float ndu = saturate(dot(N, float3(0, 1, 0)));
                float2 groundXZ = IN.positionWS.xz;
                float px = length(fwidth(IN.positionWS));
                GroundPalette ground = ownPalette(albedo);
                GroundShapes shapes = singleShape(materialShape(), materialPattern());
                // World-space derivatives for the pattern textures, taken here, outside every branch.
                float2 dpdx = ddx(groundXZ);
                float2 dpdy = ddy(groundXZ);
                [branch] if (_BiomeStrength > 0.0)
                {
                    float heightFraction = saturate((IN.positionWS.y - _MinHeight) / max(0.0001, _MaxHeight - _MinHeight));
                    float slope = 1.0 - smoothstep(0.75, 0.97, ndu);
                    GroundPalette biomes = lerpPalette(dryPalette(), forestPalette(), forestAmount(groundXZ, heightFraction, slope));
                    biomes = lerpPalette(biomes, ground, highGroundAmount(groundXZ, heightFraction, slope));
                    ground = lerpPalette(ground, biomes, _BiomeStrength);
                }
                [branch] if (_PaintMapRect.z > 0.0)
                {
                    float2 paintUV = paintedUV(groundXZ);
                    GroundPalette painted = ground;
                    GroundShapes paintedShapes = singleShape(materialShape(), materialPattern());
                    paintedShapes.wa = 0.0;
                    float total = 0.0;
                    [branch] if (any(_PaintSplatGround0))
                        addPainted(painted, paintedShapes, total, SAMPLE_TEXTURE2D_LOD(_PaintSplat0, sampler_linear_clamp, paintUV, 0), _PaintSplatGround0, albedo);
                    [branch] if (any(_PaintSplatGround1))
                        addPainted(painted, paintedShapes, total, SAMPLE_TEXTURE2D_LOD(_PaintSplat1, sampler_linear_clamp, paintUV, 0), _PaintSplatGround1, albedo);
                    [branch] if (any(_PaintSplatGround2))
                        addPainted(painted, paintedShapes, total, SAMPLE_TEXTURE2D_LOD(_PaintSplat2, sampler_linear_clamp, paintUV, 0), _PaintSplatGround2, albedo);
                    [branch] if (any(_PaintSplatGround3))
                        addPainted(painted, paintedShapes, total, SAMPLE_TEXTURE2D_LOD(_PaintSplat3, sampler_linear_clamp, paintUV, 0), _PaintSplatGround3, albedo);
                    ground = lerpPalette(ground, painted, saturate(total));
                    // The unpainted share keeps the material's shape.
                    addShape(paintedShapes, materialShape(), materialPattern(), 1.0 - saturate(total));
                    shapes = paintedShapes;
                }
                albedo = ground.base;

                // ---- Painted patches, wind streaks and pebbles, at the second shape too where two meet ----
                float second = secondShare(shapes);
                albedo = paintSurface(ground, shapes.a, shapes.pa, groundXZ, px, dpdx, dpdy);
                [branch] if (second > 0.004)
                    albedo = lerp(albedo, paintSurface(ground, shapes.b, shapes.pb, groundXZ, px, dpdx, dpdy), second);

                // ---- Painted grass (GrassField), so a field reads from afar ----
                // Before the cliff overlay: grass does not grow on cliffs, and neither does its colour.
                half cover = 0.0;
                if (_GrassMapRect.z > 0.0)
                {
                    // Warped like the tufts' paint read, so the colour's outline is organic too.
                    float2 warpUV = IN.positionWS.xz / max(0.0001, _GrassMapWarp.y);
                    float2 warp = float2(valueNoise(warpUV + 17.3), valueNoise(warpUV + 41.9)) * 2.0 - 1.0;
                    float2 grassUV = (IN.positionWS.xz + warp * _GrassMapWarp.x - _GrassMapRect.xy) * _GrassMapRect.zw;
                    half4 grass = SAMPLE_TEXTURE2D(_GrassColorMap, sampler_GrassColorMap, grassUV);
                    // Premultiplied by coverage and stored linear (GrassGroundTint): un-premultiply,
                    // then convert the materials' sRGB colours.
                    half3 grassColour = grass.rgb / max(grass.a, 1e-3);
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    grassColour = SRGBToLinear(grassColour);
                #endif
                    cover = grass.a * step(_GrassMinHeight, IN.positionWS.y);
                    // From afar the blades in front hide the ground between the tufts: every layer
                    // of tufts the view looks through closes the gaps the one before left.
                    float farCover = saturate((dist - _GrassGroundCover.x) * _GrassGroundCover.y);
                    float layers = 1.0 + _GrassGroundCover.z * farCover;
                    cover = 1.0 - pow(saturate(1.0 - cover), layers);
                    // Up close the ground between tufts is the shade at their roots, so they grow
                    // out of it; from afar it is the field's colour.
                    grassColour *= lerp(_GrassRootShade, 1.0, farCover);
                    albedo = lerp(albedo, grassColour, cover);
                }

                // ---- Slope-based cliff overlay, layered into strata ----
                float cliff = 1.0 - smoothstep(_SlopeStart, _SlopeEnd, ndu);
                float3 cliffColor = lerp(ground.cliff, ground.strata, strataTone(IN.positionWS) * _StrataStrength);
                albedo = lerp(albedo, cliffColor, cliff);

                // ---- Near ground detail: brush strokes everywhere; gravel and cracks on bare,
                // flat ground only, since grass hides them and cliffs have their strata ----
                float near = 1.0 - smoothstep(_DetailFadeStart, _DetailFadeEnd, dist);
                half4 detail = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, IN.positionWS.xz / max(0.0001, _DetailTile));
                float bare = (1.0 - cover) * (1.0 - cliff) * near;
                albedo *= 1.0 + (detail.b - 0.5) * 2.0 * _BrushStrength * near;
                // Gravel gathers in world-space clumps, which also hides the map's tiling; dried
                // cracks take the open ground between them. The ground scales both (its soil).
                float clump = smoothstep(1.0 - ground.soil.w, 1.25 - ground.soil.w,
                                         patchNoise(IN.positionWS.xz / max(0.0001, _GravelClumpSize)));
                albedo *= 1.0 - saturate(detail.g * _CrackStrength * ground.soil.y) * bare * (1.0 - clump);
                // Each stone's tone varies; its rim darkens with its height, like a cavity.
                float3 gravelColour = ground.pebble * lerp(0.55, 1.2, detail.r) * lerp(0.7, 1.0, detail.a);
                albedo = lerp(albedo, gravelColour, smoothstep(0.0, 0.25, detail.a) * saturate(_GravelStrength * ground.soil.x) * bare * clump);

                // ---- Wind ripples: lighting-only relief on flat ground, gone once sub-pixel ----
                [branch] if (ground.detail.x > 0.0)
                {
                    float2 grad = rippleSlope(groundXZ, px, shapes.a.w);
                    [branch] if (second > 0.004)
                        grad = lerp(grad, rippleSlope(groundXZ, px, shapes.b.w), second);
                    N = normalize(N + float3(-grad.x, 0.0, -grad.y) * ground.detail.x * (1.0 - cliff));
                }

                // ---- Lighting (URP main light + wrapped diffuse) ----
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float wrap = _LightWrap;
                float NdotL = saturate((dot(N, mainLight.direction) + wrap) / (1.0 + wrap));
                float3 lit  = mainLight.color * _LitTint.rgb * ToonStep(NdotL * mainLight.shadowAttenuation);

                // Additional (point/spot) lights — flashlight + any point lights affect the terrain.
            #ifdef _ADDITIONAL_LIGHTS
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = N;
                inputData.viewDirectionWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                uint addCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(addCount)
                    Light addLight = GetAdditionalLight(lightIndex, IN.positionWS);
                    float addNdotL = saturate((dot(N, addLight.direction) + wrap) / (1.0 + wrap));
                    lit += addLight.color * addNdotL * addLight.distanceAttenuation * addLight.shadowAttenuation;
                LIGHT_LOOP_END
            #endif

                // Long-throw flashlight contribution — flatter falloff than URP's
                // additional-light attenuation so distant terrain still reads.
                lit += SampleFlashlight(IN.positionWS, N, wrap);

                // SH ambient (sky-tinted) + small flat boost so shadow side reads color;
                // the shadow tint colours that lift (e.g. lilac shadows for a pastel look).
                float3 ambient = SampleSH(N) + _AmbientBoost.xxx + ground.shadow;

                float3 color = albedo * (lit + ambient);

                // ---- Rim haze (atmosphere fake) ----
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimStrength;
                color += _RimColor.rgb * rim;

                // ---- Optional distance tint (dust haze) ----
                float t = saturate((dist - _FogStart) / max(0.0001, (_FogEnd - _FogStart)));
                color = lerp(color, _FogTint.rgb, t * _FogStrength);

                // URP fog
                color = MixFog(color, IN.fogFactor);

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // Shadow caster — required for casting shadows onto self/scene.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 GetShadowPositionHClip(A IN)
            {
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            V ShadowVert(A IN)
            {
                V OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = GetShadowPositionHClip(IN);
                return OUT;
            }

            half4 ShadowFrag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // Depth-only — required for depth prepass / SSAO / etc.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            V DepthVert(A IN)
            {
                V OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // Depth + normals. With SSAO on depth-normals the renderer builds the camera depth
        // texture from THIS pass, not DepthOnly — without it the terrain is missing from that
        // texture, and the water's shore foam and see-through (which read it) vanish.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma shader_feature_local _FLAT_SHADING
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../DepthNormals.hlsl"

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            V DepthNormalsVert(A IN)
            {
                V OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 DepthNormalsFrag(V IN) : SV_Target
            {
                // The same normal the forward pass lights with, so SSAO agrees with the shading.
            #if defined(_FLAT_SHADING)
                float3 normalWS = normalize(cross(ddy(IN.positionWS), ddx(IN.positionWS)));
            #else
                float3 normalWS = IN.normalWS;
            #endif
                return EncodeDepthNormal(normalWS);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
