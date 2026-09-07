#ifndef SPACEGAME_FROZENSTATUE_INCLUDED
#define SPACEGAME_FROZENSTATUE_INCLUDED

// Shared by every pass of FrozenStatue.shader. The forward, shadow and depth passes must all
// cut the rime to exactly the same extent, or a statue that is half formed casts the shadow
// of a whole one and PastelQuantize's ink outlines a silhouette that is not on screen yet.
//
// Same rule and same failure mode as ClothWind.hlsl's displacement and FoamSurface.hlsl's
// dissolve: nothing throws, the second thing you look at is just wrong.

#include "ArtifactSubstance.hlsl"

CBUFFER_START(UnityPerMaterial)
    half4 _BandLit;
    half4 _BandUpper;
    half4 _BandLower;
    half4 _BandDeep;

    float _LightWrap;
    float _Ambient;
    float _RimLift;
    float _RimPower;
    float _CoreDarken;

    float _FlawScale;
    float _FlawDepth;
    float _FlawContrast;
    float _FlawSharpness;

    float _GlintStrength;
    float _GlintTightness;

    float _Freeze;
    float _FreezeMargin;
    float _FreezeScale;
CBUFFER_END

// The flaw field, in OBJECT space — the opposite choice from the foam, and for the opposite
// reason. Foam blobs have to share one world field so that neighbours merge; a statue is one
// object and its flaws belong to IT, so that two statues standing together do not turn out to
// be cut from the same block of ice, and so that a statue which does get nudged does not have
// its internal cracks swim through it.
//
// Ridged rather than plain: `1 - |2n - 1|` folds the noise at its midpoint, which turns soft
// blobs into creases. Ice reads as ice because of internal planes and fractures, and a soft
// cloudy field reads as wax.
float FrozenFlaws(float3 positionOS, float3 normalOS)
{
    float field = SubstanceTriplanarTurbulence(
        positionOS, normalOS, _FlawScale, _FlawSharpness);
    float ridged = 1.0 - abs(2.0 * field - 1.0);
    return lerp(field, ridged, _FlawContrast);
}

// Rime creeping over the body as it freezes, so the swap from a living thing to a statue is a
// second and a half of something happening rather than one frame of a body being replaced.
// A separate, coarser field from the flaws: the crust advances in big tongues while the
// cracks inside it stay fine, and driving both from one field ties the two scales together in
// a way that reads as the whole statue pulsing.
//
// The threshold sweeps a MARGIN past both ends of the field's own 0..1 range, because the
// field reaches both: without it a statue at Freeze 1 still has pinholes in it.
void FrozenRimeClip(float3 positionOS, float3 normalOS)
{
    float rime = SubstanceTriplanarTurbulence(
        positionOS, normalOS, _FreezeScale, _FlawSharpness);
    float threshold = lerp(1.0 + _FreezeMargin, -_FreezeMargin, _Freeze);
    clip(rime - threshold);
}

#endif // SPACEGAME_FROZENSTATUE_INCLUDED
