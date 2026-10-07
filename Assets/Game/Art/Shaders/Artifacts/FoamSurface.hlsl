#ifndef SPACEGAME_FOAMSURFACE_INCLUDED
#define SPACEGAME_FOAMSURFACE_INCLUDED

// Shared by every pass of FoamSurface.shader. The forward, shadow and depth passes must all
// cut exactly the same holes, or an expiring blob casts the shadow of a solid sphere and
// PastelQuantize's ink draws its outline around geometry that is no longer there.
//
// That is the same rule ClothWind.hlsl exists to enforce for its displacement, and it fails
// the same way: silently, and only in the second thing you look at.

#include "ArtifactSubstance.hlsl"

// Bounded so the array has a fixed size and the loop can unroll. 192 covers one player's full
// 192-dab budget; past that the uploader is expected to cull by distance rather than to raise
// this, because the cost is paid by every foam pixel on screen. It is the single most expensive
// number in this artifact — every entry costs a distance and a normalize on every foam
// fragment, and raising it again is a profiler question, not a taste one (GDC-L1-PERF-0001).
#define FOAM_MAX_BLOBS 192

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
    float _AmbientFloor;
    float _RimLift;
    float _RimPower;
    float _Backlight;

    float _BubbleScale;
    float _BubbleDepth;
    float _BubbleShade;

    float _WeldRadius;

    float _Dissolve;
    float _DissolveMargin;

    // Per-blob, written through a MaterialPropertyBlock beside _Dissolve, so twenty-four lumps
    // are still one material. It biases the shading scalar rather than tinting anything: the
    // four bands are chosen off one column of the palette lattice, and a blob nudged along the
    // ladder lands on a DIFFERENT one of those four entries — which is colour variation that
    // cannot walk off the column, unlike a tint. A mass with every lump on the same band reads
    // as one moulded object however well its normals weld.
    float _ShadeBias;
CBUFFER_END

// The bubble field, in WORLD space.
//
// Two overlapping blobs therefore sample the same field, so the surface detail runs across
// the seam instead of restarting at it — in object space each sphere would carry its own
// pattern and the seam would stay visible as a texture discontinuity even after the normal
// had been welded. The scale is per metre, which is also what lets a blob grow from nothing
// to its full radius without the pattern inflating with it: the bubbles hold their size and the
// sphere grows through them.
//
// CELLS, NOT VALUE NOISE, and that swap is most of what makes this read as foam. Two octaves of
// value noise are blotches: light and dark patches with no shape of their own, which banded into
// four flats is a mass that looks poured rather than whipped. Foam is packed round bubbles with
// dark walls between them, and a Worley field is that by construction. It also hands back the
// gradient it already computed, which is where the bump comes from — see FoamSurface.shader.
//
// It takes no normal and does no triplanar projection, unlike everything else textured in this
// game — see the header over SubstanceCells for why a cell field is the one kind that a
// triplanar blend destroys rather than merely softens.
SubstanceCellField FoamCells(float3 positionWS)
{
    return SubstanceCells(positionWS, _BubbleScale);
}

// A LUMP IS AN EXACT SPHERE, and it is worth saying why, because the obvious improvement has
// already been tried and reverted.
//
// There was a vertex displacement here (`_ShapeDepth` / `_ShapeScale`) that pushed the surface off
// the sphere so a lump had a silhouette of its own — the reasoning being that the weld bends
// NORMALS where two lumps meet, which fixes shading across a seam but leaves both outlines
// circular, and the eye counts objects by their outline.
//
// It could not be made to pay. Displacing a mesh only works where the mesh can RESOLVE the field:
// the amplitude was 21 % of the radius against a noise feature about 0.45 m across, on a sphere
// whose edges are 0.18 m — under three edges per lobe. Between vertices the surface is linear, so
// what actually reached the screen was not a lobed blob, it was a faceted one, and a mass of them
// read as a heap of flat plates. Raising the subdivision chases the frequency and never catches
// it; lowering the frequency until the mesh resolves it leaves a displacement too broad to see.
//
// The outline is broken by cheaper things that cannot facet: each lump is scaled NON-UNIFORMLY and
// tumbled (`FoamBlob.shapeSquash`, a smooth ellipsoid), the weld fuses neighbours into one
// silhouette, and the cell field textures across the join. That is what the mass reads from now.
//
// So: if a lumpier silhouette is wanted again, it is a MESH question or a raymarch question, not a
// displacement one. Do not reintroduce a per-vertex offset on this sphere.

// Eat the blob away one BUBBLE at a time as its clock runs down, so it visibly pops apart
// instead of blinking out.
//
// Driven mostly by the cell id, so a bubble leaves whole rather than being eaten through the
// middle, and partly by the wall, so the seams between bubbles go first and the mass comes apart
// along the lines it already reads as being built from.
//
// The threshold sweeps a MARGIN past both ends of the field's own 0..1 range, because the
// field does reach both ends: without it a blob at Dissolve 0 already has pinholes and one at
// Dissolve 1 still has specks left behind.
void FoamDissolveClip(SubstanceCellField cells)
{
    float field = saturate(cells.id * 0.75 + (1.0 - cells.wall) * 0.25);
    float threshold = lerp(-_DissolveMargin, 1.0 + _DissolveMargin, _Dissolve);
    clip(field - threshold);
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
