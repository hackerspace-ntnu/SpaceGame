#ifndef SPACEGAME_FROZENSTATUE_INCLUDED
#define SPACEGAME_FROZENSTATUE_INCLUDED

// Shared by every pass of FrozenStatue.shader. The forward, shadow and depth passes must all
// cut the rime to exactly the same extent, or a statue that is half formed casts the shadow
// of a whole one and PastelQuantize's ink outlines a silhouette that is not on screen yet.
//
// Same rule and same failure mode as ClothWind.hlsl's displacement and FoamSurface.hlsl's
// dissolve: nothing throws, the second thing you look at is just wrong.

#include "ArtifactSubstance.hlsl"

// Object space expressed in METRES.
//
// Both scales below are documented "per m" and neither meant it. A statue is a posed copy of
// an imported body, and every `_exportlib` FBX leaves the centimetre file scale on its node
// (`ls = (100, 100, 100)` — Nomad, Appa, CrabWalker6 and the vehicles all carry it), so a raw
// `positionOS` on a 1.8 m nomad spans about 0.018. Sampled at `_FlawScale` 7 the whole statue
// then sits inside a fraction of ONE noise cell: the ice comes out a single flat colour with
// no fractures in it, and `FrozenRimeClip` flips the entire body on or off in one step
// instead of creeping over it. Both failures are silent, and both look like a tuning problem
// rather than a units problem, which is how they survive.
//
// The columns of unity_ObjectToWorld are the world-space images of the object-space basis
// vectors, so their lengths are exactly how many metres one object-space unit spans on each
// axis. That is a derivation rather than a constant, so it needs no property and no guess: it
// is right for a centimetre FBX, for a metre one, and for a statue the game has scaled.
//
// Unlike StormCloud this needs no axis swap. Every consumer here is triplanar noise, which is
// a valid pattern in any consistent frame — turning the frame only turns the pattern.
float3 FrozenObjectMetres(float3 positionOS)
{
    float3 metresPerUnit = float3(
        length(unity_ObjectToWorld._m00_m10_m20),
        length(unity_ObjectToWorld._m01_m11_m21),
        length(unity_ObjectToWorld._m02_m12_m22));
    return positionOS * metresPerUnit;
}

CBUFFER_START(UnityPerMaterial)
    half4 _BandLit;
    half4 _BandUpper;
    half4 _BandLower;
    half4 _BandDeep;

    float _LightWrap;
    float _AmbientFloor;
    float _RimLift;
    float _RimPower;
    float _CoreDarken;

    float _FlawScale;
    float _FlawDepth;
    float _FlawShading;
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
