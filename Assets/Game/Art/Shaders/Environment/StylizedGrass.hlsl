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
//   Sheen    - a blade bent away from the eye shows its paler, duller back, so a gust rolls
//              across a field as a silvery wave; trampled grass lying flat shows it too.
//   Contact  - beside a painted tree or bush (GroundContacts) the grass is shaded toward its
//              roots, so the plant stands in the meadow instead of on it.
//   Leaves   - each blade is mostly lit as the ground, with _BladeNormalBlend of its own facing
//              mixed in, so overlapping blades read as soft scales rather than one flat sheet.
//   Morph    - toward each detail switch, a tuft eases into the next level's shape (the
//              tuft mesh's UV1/UV2 carry that shape's positions and normals), so the switch
//              itself changes nothing on screen.
//   Canopy   - from afar only the tops of the grass show, so a far tuft is coloured as its tips
//              all the way down: no dark root to outline it against the field around it.
//   Sinking  - toward the draw distance the tufts sink into the ground, so the far field is
//              just the terrain colour they were already matching.

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "GrassShared.hlsl"

StructuredBuffer<GrassVisible> _GrassVisible;

// Set per detail-level draw by GrassBiomeStream: x the jittered range (GrassLodRange) at which
// the tuft starts morphing into the next level's shape, y one over the length of that morph
// (0 = this level never morphs), z 1 when the next level is the eye-facing distant spikes.
float4 _LodMorph;

// Set per draw by GrassBiomeStream: x the distance a tuft starts taking its tip colour all the
// way down, y one over the length of that change.
float4 _GrassCanopy;

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
    float  _TrampleStrength;

    half   _SheenStrength;
    half   _SheenLift;
    half   _SheenDesaturate;
    half   _ContactShade;
CBUFFER_END

// A gust shows the sheen from this push (GrassVisible.motion.x) and in full from SHEEN_FULL_PUSH.
#define SHEEN_START_PUSH 0.35
#define SHEEN_FULL_PUSH  1.0
// Leaning across the view still shows this much of the back; leaning straight away shows all of it.
#define SHEEN_ACROSS     0.35
// Contact shade is strongest at the roots and keeps this fraction of its strength at the tips.
#define CONTACT_TIP_SHADE 0.4

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
    float  canopy;      // 0..1 how far this tuft is coloured as its tips only
    float  contact;     // 0..1 how close the tuft stands to a plant's base
    float  flatten;     // 0..1 how far a body or a trail has pressed it down
    float2 lean;        // the tip's sway per metre of blade, world XZ
};

// Turns a tuft-space vector about Y by (cos, sin).
float3 TurnTuft(float3 v, float2 turn)
{
    return float3(v.x * turn.x - v.z * turn.y, v.y, v.x * turn.y + v.z * turn.x);
}

GrassVertex PlaceGrassVertex(float3 positionOS, float3 normalOS, float3 morphOS, float3 morphNormalOS,
                             float bladeSeed, uint instanceID)
{
    GrassVisible tuft = _GrassVisible[instanceID];
    float3 root = tuft.rootHeight.xyz;
    float height = tuft.rootHeight.w;
    float2 turn = tuft.turnSway.xy;
    float2 sway = tuft.turnSway.zw;

    float morph = saturate((GrassLodRange(root, _WorldSpaceCameraPos, tuft.look.x) - _LodMorph.x) * _LodMorph.y);
    float2 morphTurn = _LodMorph.z > 0.5 ? GrassFaceEye(root, _WorldSpaceCameraPos) : turn;
    float3 shape = lerp(TurnTuft(positionOS, turn), TurnTuft(morphOS, morphTurn), morph);

    GrassVertex v;
    v.height01  = saturate(shape.y);
    v.tint      = tuft.look.x;
    float2 patchContact = GrassUnpackHalves(tuft.look.y);
    v.patch     = patchContact.x;
    v.contact   = patchContact.y;
    v.flatten   = tuft.motion.y;
    v.lean      = sway;
    v.variation = tuft.look.z;
    v.accent    = tuft.look.w;
    v.gust      = tuft.motion.x;
    v.canopy    = saturate((length(root - _WorldSpaceCameraPos) - _GrassCanopy.x) * _GrassCanopy.y);
    v.bladeSeed = frac(bladeSeed * 7.13 + v.tint * 3.71);

    float3 offset = shape * height;
    offset.y *= tuft.motion.z;
    offset.xz *= tuft.motion.w;
    v.bladeNormalWS = lerp(TurnTuft(normalOS, turn), TurnTuft(morphNormalOS, morphTurn), morph);

    float tipWeight = v.height01 * v.height01 * height;
    offset.xz += sway * tipWeight;
    // Keep the blade roughly its own length: what leans sideways comes off the top.
    offset.y = max(0.0, offset.y - length(sway) * tipWeight * 0.5) * (1.0 - tuft.motion.y * 0.55);

    v.positionWS = root + offset;
    return v;
}

// How much of a blade's paler back shows (0..1): where the wind bends it away from the eye, and
// wherever it has been pressed flat.
half GrassSheen(GrassVertex v, float3 toCamera)
{
    float leanLength = length(v.lean);
    float2 awayFromEye = -normalize(toCamera.xz + float2(1e-4, 0));
    float away = leanLength > 1e-4 ? saturate(dot(v.lean / leanLength, awayFromEye)) : 0.0;
    float windBend = smoothstep(SHEEN_START_PUSH, SHEEN_FULL_PUSH, v.gust) * lerp(SHEEN_ACROSS, 1.0, away);
    return _SheenStrength * v.height01 * max(windBend, v.flatten);
}

half3 GrassAlbedo(float height01, float patch, float tint, float variation, float bladeSeed, float gust,
                  float canopy, float contact, half sheen)
{
    // Where along the root-to-tip ramp this vertex takes its colour from.
    height01 = lerp(height01, 1.0, canopy);
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

    half3 back = lerp(colour, Luminance(colour).xxx, _SheenDesaturate) * _SheenLift;
    colour = lerp(colour, back, sheen);
    colour *= 1.0 - _ContactShade * contact * lerp(1.0, CONTACT_TIP_SHADE, height01);
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
