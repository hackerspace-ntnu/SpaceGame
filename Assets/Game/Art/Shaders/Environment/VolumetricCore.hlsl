#ifndef SPACEGAME_VOLUMETRIC_CORE_INCLUDED
#define SPACEGAME_VOLUMETRIC_CORE_INCLUDED

// The maths every volumetric effect in this project needs, and nothing that belongs to any one of
// them.
//
// All of it was written first for the sandstorm, where each piece exists because its absence was
// visible on screen. It lives here now because the fog volumes and the cloud layer need exactly
// the same things, and three copies of a phase function is three chances for two of them to drift
// apart. The sandstorm still owns its shape; only the physics moved.
//
// Nothing in this file knows what a storm, a fog volume or a cloud is. If a function needs to ask,
// it belongs in the effect's own include instead.

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
// Random.hlsl, and not by accident: InterleavedGradientNoise lives there rather than in
// Common.hlsl. Every shader that includes this file happens to pull URP's Core.hlsl first,
// which drags it in — so leaving it out works right up until the first one that does not.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

// ── Scattering ────────────────────────────────────────────────────────────────

// Henyey-Greenstein, normalised so isotropic scattering comes out at 1 and the artist parameter
// reads as "how much brighter is this when the light is behind it". Forward scattering is most of
// why dust looks like dust and mist looks like mist: a volume between you and the sun lights up,
// and the same volume with the sun behind you is a dull wall.
float VolPhaseHG(float g, float cosAngle)
{
    float gg = g * g;
    return (1.0 - gg) / pow(abs(1.0 + gg - 2.0 * g * cosAngle), 1.5);
}

// Two lobes: a strong forward one for the glow around the light, and a weak backward one for the
// silvering you get looking away from it. Real scattering media have both, and a single lobe makes
// the side of a volume facing away from the sun read as dead flat.
float VolPhaseDual(float forwardG, float backwardG, float blend, float cosAngle)
{
    return lerp(VolPhaseHG(forwardG, cosAngle), VolPhaseHG(-abs(backwardG), cosAngle), saturate(blend));
}

// Beer-Lambert, summed over a few octaves of decreasing extinction.
//
// A single term is wrong for anything thick in a way that is immediately visible: half a kilometre
// of dust has an optical depth in the hundreds, so every pixel comes back pure black and the volume
// renders as a hole in the world. Real media are bright precisely BECAUSE they are dense — light
// that cannot pass straight through bounces instead. Summing terms with geometrically decreasing
// extinction is the standard cheap stand-in: the first octave is the direct beam, the later ones
// stand in for light that took a longer, scattered route.
//
// The falloff defaults are the sandstorm's tuned values. Changing them changes that storm, so pass
// your own rather than editing these.
float VolMultiScatter(float opticalDepth, int octaves, float weightFalloff, float extinctionFalloff)
{
    float transmittance = 0.0;
    float weight = 1.0;
    float scale = 1.0;

    for (int i = 0; i < octaves; i++)
    {
        transmittance += weight * exp(-opticalDepth * scale);
        weight *= weightFalloff;
        scale *= extinctionFalloff;
    }

    return transmittance;
}

float VolMultiScatter(float opticalDepth)
{
    return VolMultiScatter(opticalDepth, 3, 0.55, 0.28);
}

// The powder term. Without it the side of a billow facing the light reads as flat and washed out,
// because a single-scattering model has no idea light has to get INTO the medium before it can come
// back out. Approximates the darkening of dense edges seen from the lit side.
float VolPowder(float density, float strength)
{
    return 1.0 - exp(-density * strength);
}

// ── Shaping ───────────────────────────────────────────────────────────────────

float VolRemap(float value, float low, float high, float newLow, float newHigh)
{
    return newLow + (value - low) / max(1e-4, high - low) * (newHigh - newLow);
}

/// Density from a coverage mask and a noise sample, the standard cloud formulation.
///
/// The analytic shape must only ever be a COVERAGE mask, never the density itself. That distinction
/// is the whole difference between a volume and a box: used directly, everything inside the feather
/// saturates to opaque and the silhouette becomes the bounding shape with a fuzzy rim. Remapping
/// the noise against coverage instead lets the noise decide the shape, with coverage deciding only
/// how much of it survives — all of it in the core, only the peaks at the edges.
float VolCoverageRemap(float noise, float coverage)
{
    return saturate((noise - (1.0 - coverage)) / max(0.001, coverage));
}

/// Carves an existing density with a detail octave. `erosion` is how hard the detail bites.
float VolErode(float density, float detail, float erosion)
{
    float carve = (1.0 - detail) * erosion;
    return saturate((density - carve) / max(0.001, 1.0 - carve));
}

/// A cheap curl-like domain warp — the thing that makes a volume look alive rather than scrolled.
///
/// Drifting noise along a wind vector slides the whole mass past you like a texture on a conveyor.
/// Real air also turns over in place. Three sines cost a handful of instructions and buy most of
/// that: the field is divergence-light, so the warp stirs the noise rather than stretching it.
float3 VolBreathe(float3 positionWS, float time, float amount, float scale)
{
    if (amount <= 0.0)
        return 0.0;

    float3 s = positionWS / max(1.0, scale) + time;
    return float3(sin(s.y) + sin(s.z),
                  sin(s.z) + sin(s.x),
                  sin(s.x) + sin(s.y)) * amount;
}

// ── Ray intersection ──────────────────────────────────────────────────────────
//
// The `inout` slab tests narrow an interval that starts at [0, huge] and return whether anything is
// left of it. The `out` whole-shape tests set the interval themselves.

/// One pair of parallel planes with the given 2D normal. Used for a wall's thickness and its ends.
bool VolSlab2D(float2 origin, float2 direction, float2 normal, float halfWidth,
               inout float tNear, inout float tFar)
{
    float along = dot(direction, normal);
    float offset = dot(origin, normal);

    if (abs(along) < 1e-6)
        return abs(offset) <= halfWidth;   // parallel: either always inside or never

    float t0 = (-halfWidth - offset) / along;
    float t1 = (halfWidth - offset) / along;

    tNear = max(tNear, min(t0, t1));
    tFar = min(tFar, max(t0, t1));
    return tFar > tNear;
}

/// One pair of parallel planes perpendicular to an axis.
bool VolSlab1D(float origin, float direction, float low, float high,
               inout float tNear, inout float tFar)
{
    if (abs(direction) < 1e-6)
        return origin >= low && origin <= high;

    float t0 = (low - origin) / direction;
    float t1 = (high - origin) / direction;

    tNear = max(tNear, min(t0, t1));
    tFar = min(tFar, max(t0, t1));
    return tFar > tNear;
}

/// Axis-aligned box. In a volume's own local space this is also the test for an oriented box.
bool VolRayBox(float3 ro, float3 rd, float3 boxMin, float3 boxMax, out float tNear, out float tFar)
{
    tNear = -1e9;
    tFar = 1e9;

    bool hit = VolSlab1D(ro.x, rd.x, boxMin.x, boxMax.x, tNear, tFar)
            && VolSlab1D(ro.y, rd.y, boxMin.y, boxMax.y, tNear, tFar)
            && VolSlab1D(ro.z, rd.z, boxMin.z, boxMax.z, tNear, tFar);

    tNear = max(tNear, 0.0);
    return hit && tFar > tNear;
}

/// Sphere. In local space this is also the test for an ellipsoid.
bool VolRaySphere(float3 ro, float3 rd, float3 center, float radius, out float tNear, out float tFar)
{
    tNear = 0.0;
    tFar = 0.0;

    float3 oc = ro - center;
    float a = dot(rd, rd);
    float b = 2.0 * dot(oc, rd);
    float c = dot(oc, oc) - radius * radius;

    if (a < 1e-9)
        return false;

    float disc = b * b - 4.0 * a * c;
    if (disc < 0.0)
        return false;

    float root = sqrt(disc);
    tNear = max((-b - root) / (2.0 * a), 0.0);
    tFar = (-b + root) / (2.0 * a);
    return tFar > tNear;
}

/// Capped cylinder about the local Y axis, between y = low and y = high.
bool VolRayCylinderY(float3 ro, float3 rd, float radius, float low, float high,
                     out float tNear, out float tFar)
{
    tNear = -1e9;
    tFar = 1e9;

    if (!VolSlab1D(ro.y, rd.y, low, high, tNear, tFar))
        return false;

    float a = dot(rd.xz, rd.xz);
    float b = 2.0 * dot(ro.xz, rd.xz);
    float c = dot(ro.xz, ro.xz) - radius * radius;

    if (a < 1e-9)
    {
        // Straight up or down the axis: inside for all t, or never.
        if (c > 0.0)
            return false;
    }
    else
    {
        float disc = b * b - 4.0 * a * c;
        if (disc < 0.0)
            return false;

        float root = sqrt(disc);
        tNear = max(tNear, (-b - root) / (2.0 * a));
        tFar = min(tFar, (-b + root) / (2.0 * a));
    }

    tNear = max(tNear, 0.0);
    return tFar > tNear;
}

/// The gap between two concentric spheres — the shell a cloud layer occupies.
///
/// Returns the near and far bound of the part of the ray inside the shell. A ray from under the
/// inner sphere (which is where a player always is) enters at the inner sphere and leaves at the
/// outer one; a ray looking down never reaches the shell at all. Getting this from a sphere rather
/// than from two horizontal planes is the whole reason the clouds curve down to the horizon
/// instead of stretching to infinity like a ceiling.
bool VolRayShell(float3 ro, float3 rd, float3 center, float innerRadius, float outerRadius,
                 out float tNear, out float tFar)
{
    tNear = 0.0;
    tFar = 0.0;

    float outerNear, outerFar;
    if (!VolRaySphere(ro, rd, center, outerRadius, outerNear, outerFar))
        return false;

    float distanceToCenter = length(ro - center);
    float innerNear, innerFar;
    bool hitsInner = VolRaySphere(ro, rd, center, innerRadius, innerNear, innerFar);

    if (distanceToCenter < innerRadius)
    {
        // Standing on the ground, under the layer: march from where the ray leaves the inner
        // sphere to where it leaves the outer one.
        tNear = hitsInner ? innerFar : 0.0;
        tFar = outerFar;
    }
    else if (distanceToCenter < outerRadius)
    {
        // Inside the layer itself — flying through the clouds.
        tNear = 0.0;
        tFar = hitsInner ? innerNear : outerFar;
    }
    else
    {
        // Above the layer, looking down through it.
        tNear = outerNear;
        tFar = hitsInner ? innerNear : outerFar;
    }

    tNear = max(tNear, 0.0);
    return tFar > tNear;
}

// ── Sampling ──────────────────────────────────────────────────────────────────

/// Per-pixel offset for the first march sample.
///
/// Without it a low step count bands into rings that no amount of grain hides, because every pixel
/// samples the volume at the same set of depths. Interleaved gradient noise is the cheapest offset
/// that does not itself look like a pattern.
float VolJitter(float2 pixelCoord)
{
    return InterleavedGradientNoise(pixelCoord, 0);
}

float VolHash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

#endif
