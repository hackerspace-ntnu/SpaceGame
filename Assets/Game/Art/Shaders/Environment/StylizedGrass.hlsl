#ifndef SPACEGAME_STYLIZEDGRASS_INCLUDED
#define SPACEGAME_STYLIZEDGRASS_INCLUDED

// Shared by every pass of StylizedGrass.shader. The forward, depth and depth-normals passes
// must place each blade identically, or the water's depth foam and SSAO see a different meadow
// from the one on screen.
//
// One draw is one detail level of one biome: the mesh is a single tuft, and SV_InstanceID
// indexes _GrassVisible, the tufts GrassCull.compute kept this frame and already animated.
// There is no object-to-world matrix at all.
//
//   Gradient - root colour (which matches the ground under it) to tip colour, so the meadow
//              grows out of the terrain instead of sitting on it.
//   Patches  - a slow world-space noise drifts large areas toward _PatchColor, the yellow-green
//              breakup that makes a field read as a field and not a carpet.
//   Variety  - tips lean toward one of two variation colours in clumps a few metres across,
//              so the field is a mix of greens rather than one green; and every blade carries
//              its own seed (UV0.x of the tuft mesh) for a brightness jitter and a warm cast.
//   Wind     - posed per tuft by the compute pass; where the waves peak the tips lean further
//              AND lighten, so the flow reads from afar.
//   Leaves   - each blade is mostly lit as the ground, with _BladeNormalBlend of its own facing
//              mixed in, so overlapping blades read as soft scales rather than one flat sheet.
//   Sinking  - toward the draw distance the tufts sink into the ground, so the far field is
//              just the terrain colour they were already matching.

#include "GrassShared.hlsl"

StructuredBuffer<GrassVisible> _GrassVisible;

CBUFFER_START(UnityPerMaterial)
    half4  _RootColor;
    half4  _TipColor;
    half4  _PatchColor;
    half4  _VariationColorA;
    half4  _VariationColorB;
    float  _VariationScale;
    half   _VariationStrength;
    half4  _AccentColorA;
    half4  _AccentColorB;
    half   _GradientPower;
    float  _PatchScale;
    half   _PatchStrength;
    half   _TintVariation;
    half   _AccentSize;
    half4  _BladeTintColor;
    half   _BladeTintAmount;
    half   _BladeBrightness;

    half   _LightWrap;
    half   _BladeNormalBlend;
    half   _AmbientStrength;
    half   _ShadowStrength;
    half   _Translucency;

    // Read by GrassCull.compute from the material; declared here so every property is in
    // the per-material buffer, which the SRP batcher requires.
    float  _WindStrength;
    float  _WindFrequency;
    float  _WindSpeed;
    float  _WaveLength;
    float  _WaveSpeed;
    float  _FlutterStrength;
    float  _FlutterFrequency;
    half   _GustBrighten;
    float  _BendStrength;
CBUFFER_END

struct GrassVertex
{
    float3 positionWS;
    float3 bladeNormalWS;   // the blade's own facing, rotated with the tuft
    float  height01;    // 0 at the root, 1 at the tip
    float  patch;       // 0..1 patch mask, constant across a tuft
    float  tint;        // per-tuft brightness variation
    float  variation;   // 0..1 which variation colour this tuft's tips lean toward
    float  accent;      // > 0 on the rare tuft that carries a flower
    float  bladeSeed;   // 0..1 per blade within the tuft, mixed with the tuft's own tint
    float  gust;        // 0..1 how hard the wind is pushing this tuft right now
};

GrassVertex PlaceGrassVertex(float3 positionOS, float3 normalOS, float bladeSeed, uint instanceID)
{
    GrassVisible tuft = _GrassVisible[instanceID];
    float3 root = tuft.rootHeight.xyz;
    float height = tuft.rootHeight.w;
    float c = tuft.turnSway.x;
    float s = tuft.turnSway.y;
    float2 sway = tuft.turnSway.zw;

    GrassVertex v;
    v.height01  = saturate(positionOS.y);
    v.tint      = tuft.look.x;
    v.patch     = tuft.look.y;
    v.variation = tuft.look.z;
    v.accent    = tuft.look.w;
    v.gust      = tuft.motion.x;
    v.bladeSeed = frac(bladeSeed * 7.13 + v.tint * 3.71);

    float3 local = positionOS * height;
    local.y *= tuft.motion.z;
    local.xz *= tuft.motion.w;
    float3 offset = float3(local.x * c - local.z * s, local.y, local.x * s + local.z * c);
    v.bladeNormalWS = float3(normalOS.x * c - normalOS.z * s, normalOS.y, normalOS.x * s + normalOS.z * c);

    float tipWeight = v.height01 * v.height01 * height;
    offset.xz += sway * tipWeight;
    // Keep the blade roughly its own length: what leans sideways comes off the top.
    offset.y = max(0.0, offset.y - length(sway) * tipWeight * 0.5) * (1.0 - tuft.motion.y * 0.55);

    v.positionWS = root + offset;
    return v;
}

half3 GrassAlbedo(float height01, float patch, float tint, float variation, float bladeSeed, float gust)
{
    half3 varied = lerp(_VariationColorA.rgb, _VariationColorB.rgb, smoothstep(0.25, 0.75, variation));
    half3 tip = lerp(_TipColor.rgb, varied, _VariationStrength);
    half3 colour = lerp(_RootColor.rgb, tip, pow(saturate(height01), _GradientPower));
    half patchMask = smoothstep(0.42, 0.78, patch) * _PatchStrength;
    colour = lerp(colour, lerp(_RootColor.rgb, _PatchColor.rgb, height01), patchMask);
    colour *= 1.0 + (tint - 0.5) * _TintVariation;

    // Every blade a little different: a brightness jitter, and a warm reddish cast on some.
    colour *= 1.0 + (frac(bladeSeed * 13.7) - 0.5) * _BladeBrightness;
    colour = lerp(colour, _BladeTintColor.rgb * (0.7 + 0.3 * height01), bladeSeed * bladeSeed * _BladeTintAmount);
    colour *= 1.0 + (gust - 0.4) * _GustBrighten * height01;
    return colour;
}

// The flower is decided per pixel so its edge stays crisp across a blade's last segment.
half3 GrassFlower(half3 albedo, float accent, float height01)
{
    if (accent > 0 && height01 > 1.0 - _AccentSize)
        return lerp(_AccentColorA.rgb, _AccentColorB.rgb, frac(accent * 7.31));
    return albedo;
}

#endif
