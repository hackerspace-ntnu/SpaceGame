#ifndef SPACEGAME_STYLIZEDFOLIAGE_INCLUDED
#define SPACEGAME_STYLIZEDFOLIAGE_INCLUDED

// Shared by every pass of StylizedFoliage.shader, so the forward, shadow, depth and
// depth-normals passes all place a leaf in the same spot.
//
// Trees and bushes are ordinary meshes (one draw per renderer, instanced where Unity can), whose
// per-vertex wind and look data is baked by the vegetation .blend into four UV channels:
//
//   UV0  x sway weight  0 at the root .. 1 at the top      y flutter  0 at a leaf's base .. 1 at its tip
//   UV1  x limb seed    phase of the limb's own sway       y limb weight  0 at its junction .. 1 at its end
//   UV2  x height01     for the colour gradient            y leaf seed  per-leaf colour jitter and accents
//   UV3  x shell        0 deep inside the canopy .. 1 on its outside (1 on bark)
//
// The normals are not the leaves' own: the .blend gives every leaf the normal of the canopy
// blob it sits on, so a crown shades as a few soft round masses — the same trick that lights
// every grass blade as the ground it grows from.
//
//   Wind    - the whole plant leans on the wave GrassField's meadow rides (WindPush, same
//             globals), each limb sways on its own phase, and leaves flutter, harder in gusts.
//   Benders - bodies part a bush the way they part the grass, and its leaves shake as one
//             pushes through it (harder the faster it moves).
//   Colour  - inner to outer (and low to high) gradient, a per-plant variation colour so
//             neighbours differ, a shade per limb so a crown reads as several clumps, per-leaf
//             jitter and warm tint, accents at some leaf tips (blossom, fruit, an optional
//             glow), and gusts brighten the outer leaves as they do the grass tips.

#include "GrassShared.hlsl"

// Published by GrassField for every camera it draws: one wind for the meadow and everything
// growing in it. Without a GrassField in the world they stay zero and the foliage stands still.
float4 _FoliageWind;          // xy direction (world XZ), z strength scale
float  _FoliageTime;
float4 _FoliageBenders[8];    // xyz position, w radius
float4 _FoliageBenderMotion[8]; // xy heading on the ground, z speed m/s
float  _FoliageBenderCount;

// A body moving through a bush at this speed (m/s) or faster shakes its leaves fully.
#define RUSTLE_FULL_SPEED 2.0
// Leaves shake this many times faster than they flutter in the wind: a rustle, not a sway.
#define RUSTLE_PACE 2.3

CBUFFER_START(UnityPerMaterial)
    half4  _InnerColor;
    half4  _OuterColor;
    half   _ShellWeight;
    half   _HeightWeight;
    half   _GradientPower;
    half4  _VariationColorA;
    half4  _VariationColorB;
    half   _VariationStrength;
    half   _LeafBrightness;
    half4  _LeafTintColor;
    half   _LeafTintAmount;
    half   _ClumpBrightness;
    half4  _AccentColorA;
    half4  _AccentColorB;
    half   _AccentChance;
    half   _AccentSize;
    half   _AccentEmission;

    half   _LightWrap;
    half   _AmbientStrength;
    half   _InnerOcclusion;
    half   _ShadowStrength;
    half   _Translucency;

    float  _WindStrength;
    float  _WaveLength;
    float  _WaveSpeed;
    float  _WindFrequency;
    float  _WindSpeed;
    float  _LimbSway;
    float  _LimbFrequency;
    float  _FlutterStrength;
    float  _FlutterFrequency;
    half   _GustBrighten;
    float  _BendStrength;
    float  _BendRustle;
    float  _SurgeStrength;
    float  _SurgePeriod;
    float  _SurgeLength;

    float  _BarkTiling;
    half   _BarkStrength;
    half4  _GrooveColor;
    half   _GrooveStrength;
    half4  _MossColor;
    half   _MossAmount;
    half   _MossHeight;
CBUFFER_END

TEXTURE2D(_BarkMap);
SAMPLER(sampler_BarkMap);

struct FoliageAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 wind       : TEXCOORD0;
    float2 limb       : TEXCOORD1;
    float2 height     : TEXCOORD2;
    float2 shell      : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct FoliageVertex
{
    float3 positionWS;
    float3 normalWS;
    float  height01;
    float  shell;
    float  leafSeed;
    float  limbSeed;
    float  alongLeaf;   // 0 at a leaf's base .. 1 at its tip (the flutter weight)
    float  variation;   // 0..1 per plant, from where it stands
    float  gust;        // 0..1 how hard the wind is pushing here right now
};

// How far the benders push this point sideways (xy, in units of _BendStrength), and how hard a
// moving one shakes the leaves here (z, 0..1).
float3 FoliageBend(float3 positionWS)
{
    float2 push = 0;
    float rustle = 0;
    int count = (int)_FoliageBenderCount;
    for (int i = 0; i < count; i++)
    {
        float4 bender = _FoliageBenders[i];
        float3 delta = positionWS - bender.xyz;
        float reach = saturate(1.0 - length(delta.xz) / bender.w)
                    * saturate(1.0 - abs(delta.y) / (bender.w * 2.0));
        push += normalize(delta.xz + 1e-4) * reach;
        rustle = max(rustle, reach * saturate(_FoliageBenderMotion[i].z / RUSTLE_FULL_SPEED));
    }
    return float3(push, rustle);
}

FoliageVertex PlaceFoliageVertex(FoliageAttributes IN)
{
    float3 root = TransformObjectToWorld(float3(0, 0, 0));
    float3 p = TransformObjectToWorld(IN.positionOS.xyz);

    FoliageVertex v;
    v.normalWS  = TransformObjectToWorldNormal(IN.normalOS);
    v.height01  = IN.height.x;
    v.shell     = IN.shell.x;
    v.leafSeed  = IN.height.y;
    v.limbSeed  = IN.limb.x;
    v.alongLeaf = IN.wind.y;
    v.variation = GrassHash(root.xz * 0.173 + 5.3);

    float2 windDir = normalize(_FoliageWind.xy + float2(1e-5, 0));
    float3 windDir3 = float3(windDir.x, 0, windDir.y);
    float strength = _FoliageWind.z;

    // The whole plant leans on the meadow's wave, read at its root so it moves as one body:
    // the grass's own tip-weighted lean, sway weight × height above the root.
    float rootGust;
    float push = WindPush(root.xz, windDir, _FoliageTime, _WaveLength, _WaveSpeed,
                          _WindFrequency, _WindSpeed, rootGust);
    float rise = max(0.0, p.y - root.y);
    float2 lean = windDir * (_WindStrength * strength * (0.15 + push)) * (IN.wind.x * rise);
    // Under water the swell rocks the plant to and fro along the current, whatever the wind's strength.
    lean += windDir * (_SurgeStrength * WaterSurge(root.xz, windDir, _FoliageTime, _SurgePeriod, _SurgeLength))
          * (IN.wind.x * rise);
    float3 bend = FoliageBend(p);
    lean += bend.xy * (_BendStrength * IN.wind.x);
    // Keep the plant roughly its own height: what leans sideways comes off the top.
    float3 offset = float3(lean.x, -dot(lean, lean) / max(2.0 * rise, 0.5), lean.y);

    // Each limb sways on its own phase, more toward its end, in a lazy figure-eight.
    float limbPhase = _FoliageTime * _LimbFrequency + IN.limb.x * 6.2831;
    float3 limbMove = windDir3 * sin(limbPhase)
                    + float3(-windDir.y, 0.35, windDir.x) * (0.6 * sin(limbPhase * 1.37 + 1.1));
    offset += limbMove * (_LimbSway * IN.limb.y * strength * (0.5 + push));

    // Leaves flutter about their base, hardest where a gust is passing through the crown.
    v.gust = WindGust(p.xz, windDir, _FoliageTime, _WindFrequency, _WindSpeed);
    float flutterPhase = _FoliageTime * _FlutterFrequency + v.leafSeed * 6.2831 + dot(p.xz, windDir) * 0.7;
    offset += (v.normalWS * sin(flutterPhase) + windDir3 * (0.5 * cos(flutterPhase * 1.3)))
            * (_FlutterStrength * IN.wind.y * strength * (0.3 + v.gust));
    // A body pushing through shakes the leaves whatever the wind is doing.
    offset += v.normalWS * (sin(flutterPhase * RUSTLE_PACE) * _BendRustle * IN.wind.y * bend.z);

    v.positionWS = p + offset;
    return v;
}

half3 FoliageAlbedo(FoliageVertex v)
{
    half t = pow(saturate(v.shell * _ShellWeight + v.height01 * _HeightWeight), _GradientPower);
    half3 varied = lerp(_VariationColorA.rgb, _VariationColorB.rgb, smoothstep(0.2, 0.8, v.variation));
    half3 outer = lerp(_OuterColor.rgb, varied, _VariationStrength);
    half3 colour = lerp(_InnerColor.rgb, outer, t);

    // Every leaf a little different: a brightness jitter, and a warm cast on some.
    colour *= 1.0 + (frac(v.leafSeed * 13.7) - 0.5) * _LeafBrightness;
    colour = lerp(colour, _LeafTintColor.rgb * (0.7 + 0.3 * t), v.leafSeed * v.leafSeed * _LeafTintAmount);

    // Each limb's clump a shade lighter or darker, so a crown reads as several masses.
    colour *= 1.0 + (frac(v.limbSeed * 5.37) - 0.5) * _ClumpBrightness;

    colour *= 1.0 + (v.gust - 0.4) * _GustBrighten * v.shell;
    return colour;
}

// Accent hue for this leaf (blossom, fruit, glow), 0 for none. Only outer leaves carry one.
float FoliageAccent(FoliageVertex v)
{
    return v.shell > 0.35 && frac(v.leafSeed * 31.37) < _AccentChance ? 0.01 + frac(v.leafSeed * 7.31) : 0.0;
}

// The accent is decided per pixel, like the grass's flowers, so it is a crisp patch at the
// leaf's tip (_AccentSize 1 colours the whole leaf). Returns the albedo and the glow.
half3 FoliageAccentAlbedo(half3 albedo, float accent, float alongLeaf, out half3 glow)
{
    glow = 0;
    if (accent > 0 && alongLeaf >= 1.0 - _AccentSize)
    {
        half3 colour = lerp(_AccentColorA.rgb, _AccentColorB.rgb, accent);
        glow = colour * _AccentEmission;
        return colour;
    }
    return albedo;
}

// Painted bark, projected three ways from the vertex's REST offset to the plant's pivot (world
// axes, metres), so it rides the sway instead of swimming over it. The map is R painted value
// (0.5 = the material's colour), G seam (grooves, pores, veins or seeds), B height. Moss climbs
// from the roots on upward faces, filling the seams first.
half3 BarkAlbedo(half3 albedo, float3 barkPos, half3 normalWS, half height01)
{
    half3 w = pow(abs(normalWS), 4.0);
    w /= dot(w, 1.0);
    float3 p = barkPos * _BarkTiling;
    half3 bark = SAMPLE_TEXTURE2D(_BarkMap, sampler_BarkMap, p.zy).rgb * w.x
               + SAMPLE_TEXTURE2D(_BarkMap, sampler_BarkMap, p.xz).rgb * w.y
               + SAMPLE_TEXTURE2D(_BarkMap, sampler_BarkMap, p.xy).rgb * w.z;

    half3 colour = albedo * lerp(1.0, bark.r * 2.0, _BarkStrength);
    colour = lerp(colour, _GrooveColor.rgb, bark.g * _GrooveStrength);

    half reach = saturate(1.0 - height01 / _MossHeight) + saturate(normalWS.y) * 0.6;
    half moss = smoothstep(0.55, 0.7, _MossAmount * reach + bark.g * 0.35 - bark.b * 0.25);
    return lerp(colour, _MossColor.rgb * (0.75 + 0.5 * bark.r), moss);
}

#endif
