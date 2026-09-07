#ifndef SPACEGAME_FOAMSURFACE_INCLUDED
#define SPACEGAME_FOAMSURFACE_INCLUDED

// Shared by every pass of FoamSurface.shader. The forward, shadow and depth passes must all
// cut exactly the same holes, or an expiring blob casts the shadow of a solid sphere and
// PastelQuantize's ink draws its outline around geometry that is no longer there.
//
// That is the same rule ClothWind.hlsl exists to enforce for its displacement, and it fails
// the same way: silently, and only in the second thing you look at.

#include "ArtifactSubstance.hlsl"

// Bounded so the array has a fixed size and the loop can unroll. 32 covers one player's full
// 24-dab budget with room for the nearest few of someone else's; past that the uploader is
// expected to cull by distance rather than to raise this, because the cost is paid by every
// foam pixel on screen.
#define FOAM_MAX_BLOBS 32

// GLOBALS, not per material: every blob shades against the same field, and a per-material
// copy would be one field per blob. See the header of FoamSurface.shader for the contract.
float4 _FoamBlobs[FOAM_MAX_BLOBS];   // xyz = world centre, w = world radius
int _FoamBlobCount;

CBUFFER_START(UnityPerMaterial)
    half4 _BandLit;
    half4 _BandUpper;
    half4 _BandLower;
    half4 _BandShadow;

    float _LightWrap;
    float _Ambient;
    float _RimLift;
    float _RimPower;
    float _Backlight;

    float _BubbleScale;
    float _BubbleDepth;
    float _BubbleSharpness;

    float _WeldRadius;

    float _Dissolve;
    float _DissolveMargin;
CBUFFER_END

// The bubble field, in WORLD space.
//
// Two overlapping blobs therefore sample the same field, so the surface detail runs across
// the seam instead of restarting at it — in object space each sphere would carry its own
// pattern and the seam would stay visible as a texture discontinuity even after the normal
// had been welded. The scale is per metre, which is also what lets a blob grow from nothing
// to 0.45 m without the pattern inflating with it: the bubbles hold their size and the sphere
// grows through them.
float FoamBubbles(float3 positionWS, float3 normalWS)
{
    return SubstanceTriplanarTurbulence(positionWS, normalWS, _BubbleScale, _BubbleSharpness);
}

// Eat the blob away from the bubbles outward as its clock runs down, so it visibly pops apart
// instead of blinking out.
//
// The threshold sweeps a MARGIN past both ends of the field's own 0..1 range, because the
// field does reach both ends: without it a blob at Dissolve 0 already has pinholes and one at
// Dissolve 1 still has specks left behind.
void FoamDissolveClip(float bubbles)
{
    float threshold = lerp(-_DissolveMargin, 1.0 + _DissolveMargin, _Dissolve);
    clip(bubbles - threshold);
}

// The blended gradient of the smooth union of every live blob, at a world point.
//
// Polynomial smooth-minimum, the same field PortalSplat.shader welds its lobes with; this is
// the 3D form of it, over spheres instead of over disc lobes. The gradient is carried
// alongside the distance and blended by the SAME weight the distance is, which is the
// standard cheap approximation: it drops the term from differentiating the blend weight
// itself, and what that term contributes is a hair of extra bulge exactly where the surface
// is already bulging.
//
// Costs one distance and one normalize per blob, so it early-outs on blobs whose surface is
// further than the weld radius from this fragment: they cannot bend a normal here, and at
// 0.45 m blobs most of the array is exactly that.
float3 FoamWeldedNormal(float3 positionWS, float3 fallbackNormal)
{
    if (_FoamBlobCount <= 0)
    {
        return fallbackNormal;
    }

    float weld = max(_WeldRadius, 1e-3);
    float bestDistance = 0.0;
    float3 bestGradient = fallbackNormal;
    bool found = false;

    for (int i = 0; i < FOAM_MAX_BLOBS; i++)
    {
        if (i >= _FoamBlobCount)
        {
            break;
        }

        float3 toCentre = positionWS - _FoamBlobs[i].xyz;
        float centreDistance = length(toCentre);
        float surfaceDistance = centreDistance - _FoamBlobs[i].w;

        if (surfaceDistance > weld)
        {
            continue;
        }

        float3 gradient = toCentre / max(centreDistance, 1e-4);

        if (!found)
        {
            bestDistance = surfaceDistance;
            bestGradient = gradient;
            found = true;
            continue;
        }

        float h = saturate(0.5 + 0.5 * (bestDistance - surfaceDistance) / weld);
        bestGradient = normalize(lerp(bestGradient, gradient, h));
        bestDistance = lerp(bestDistance, surfaceDistance, h) - weld * h * (1.0 - h);
    }

    return found ? bestGradient : fallbackNormal;
}

#endif // SPACEGAME_FOAMSURFACE_INCLUDED
