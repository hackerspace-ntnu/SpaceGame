// The aperture's outline, in HLSL.
//
// The mirror of PortalStencil.cs, and it has to STAY a mirror: PORTAL_SMOOTH below and the fold
// order of the smooth minimum are the same on both sides, so the edge the player can see is the
// edge they can walk through. Retune one and retune the other in the same commit.
//
// EVERYTHING HERE IS IN METRES. That is the whole point of the file and it was learned the hard
// way: the first version handed the shaders a coordinate normalised against the shape's own
// inscribed radius, which GROWS as paint is added — so every blob resized the rim band, deepened
// the throat and stretched the vortex across paint that had not changed, and the whole aperture
// appeared to breathe while you sprayed it. A signed distance in metres does not move when the
// shape grows somewhere else, so the paint you laid a second ago still looks exactly the way it
// did. What varies with the shape is now exactly one number, _Depth, and it is deliberately the
// radius the stroke is sprayed at rather than anything that grows.
//
// _DabCount of zero means the aperture is the ellipse inscribed in _Ellipse, which is what every
// aperture placed in a scene by hand and every aperture in a pre-spray save file is. Both branches
// return a metric distance, so nothing downstream has to know which one it got.
#ifndef PORTAL_STENCIL_INCLUDED
#define PORTAL_STENCIL_INCLUDED

#define PORTAL_MAX_DABS 24

// Deliberately outside UnityPerMaterial. An array in that cbuffer is not something the SRP batcher
// supports, so declaring one there would fail rather than merely opt out — and opting out costs
// nothing worth counting, there being at most four portal quads on screen at once.
float4 _Dabs[PORTAL_MAX_DABS];
float  _DabCount;
float  _Depth;
float2 _Centroid;
float2 _Extents;
float2 _Ellipse;

// Must equal PortalStencil.Smoothing.
#define PORTAL_SMOOTH 0.35

float PortalSmoothMin(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (b - a) / k);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

// Signed distance to the aperture's edge, in METRES. Negative inside.
float PortalStencilField(float2 q)
{
    if (_DabCount < 0.5)
    {
        // The same approximation PortalStencil.Field uses: exact on the boundary, which is the only
        // place anything reads it as a yes or a no, and cheap everywhere else.
        float2 semi = max(_Ellipse, 1e-4);
        return (length(q / semi) - 1.0) * min(semi.x, semi.y);
    }

    float field = length(q - _Dabs[0].xy) - _Dabs[0].z;

    int count = min((int)_DabCount, PORTAL_MAX_DABS);
    for (int i = 1; i < count; i++)
        field = PortalSmoothMin(field, length(q - _Dabs[i].xy) - _Dabs[i].z, PORTAL_SMOOTH);

    return field;
}

// The aperture in one call: the metric distance at this fragment, plus the angle around the middle
// of the paint that the swirl and the crawling rim are both drawn in.
float PortalStencilDistance(float2 uv, out float angle)
{
    float2 q = (uv * 2.0 - 1.0) * _Extents;
    float2 c = q - _Centroid;

    angle = atan2(c.y, c.x);
    return PortalStencilField(q);
}

#endif
