#ifndef SPACEGAME_VOLUMETRIC_FOG_INCLUDED
#define SPACEGAME_VOLUMETRIC_FOG_INCLUDED

// A body of air that is somewhere, has a shape, and has a colour.
//
// The sandstorm proved the physics; this file is the same physics applied to volumes an artist
// places by hand. What is new here is that there are SEVERAL of them at once, and that they have to
// mix — two overlapping fogs must read as one body of air whose colour is somewhere between them,
// not as two silhouettes fighting over the same pixels.
//
// Three decisions do most of the work:
//
//   * There is ONE march for all volumes, not one per volume. Everything a ray passes through is
//     integrated in the same pass, in depth order by construction, so overlap is correct for free
//     and there is no sorting problem to get wrong.
//   * Every volume's look is averaged at each sample WEIGHTED BY ITS OWN DENSITY. Where a red fog
//     is thick and a blue one is a wisp, the sample is nearly red; halfway between, it is violet.
//   * The noise is sampled in WORLD space, never in the volume's local space. Local-space noise
//     scales with the volume, so two volumes with identical settings but different sizes show
//     visibly different grain, and their overlap shows the seam between two noise fields. World
//     space makes every volume a window onto the same turbulent air.

#include "VolumetricCore.hlsl"

#define FOG_MAX_VOLUMES 8
#define FOG_MAX_LIGHTS 8

#define FOG_SHAPE_ELLIPSOID 0
#define FOG_SHAPE_BOX 1
#define FOG_SHAPE_CYLINDER 2
#define FOG_SHAPE_GROUND 3

TEXTURE3D(_FogNoiseTex);
SAMPLER(sampler_FogNoiseTex);

// Filled by FogVolumes.Push. Every array is indexed by the same volume index.
float4x4 _FogWorldToLocal[FOG_MAX_VOLUMES];
float4 _FogColor[FOG_MAX_VOLUMES];      // rgb albedo, a density scale
float4 _FogEmission[FOG_MAX_VOLUMES];   // rgb emission, a ambient fraction
float4 _FogShape[FOG_MAX_VOLUMES];      // x kind, y edge feather, z vertical falloff, w extinction
float4 _FogNoise[FOG_MAX_VOLUMES];      // x noise scale (m), y erosion, z vertical squash, w detail scale
float4 _FogMotion[FOG_MAX_VOLUMES];     // xyz drift (m, already scrolled), w anisotropy
float4 _FogBreathe[FOG_MAX_VOLUMES];    // x amount, y scale (m), z phase, w unused
int _FogVolumeCount;

float4 _FogLightPosition[FOG_MAX_LIGHTS]; // xyz world position, w inverse squared range
float4 _FogLightColor[FOG_MAX_LIGHTS];    // rgb colour premultiplied by intensity
int _FogLightCount;

// Sky radiance, pushed from C#. Read rather than sampled for the same reason the sandstorm does it:
// a fullscreen blit is a procedural draw with no per-object SH constants, so SampleSH returns zero
// and the fog comes out black. Inside a thick volume this is most of the light there is.
float4 _FogSkyLight;

float _FogSteps;
float _FogLightSteps;
float _FogMaxDistance;
float _FogTime;

// xy = one texel of the reduced-resolution march target in uv, zw = that target's size in pixels.
// Both passes need it: the composite to find the texel centres it is filtering between, and the
// march to jitter once per texel it actually writes rather than once per full-resolution pixel.
float4 _FogTexelSize;

// ── Shape ─────────────────────────────────────────────────────────────────────
//
// Every shape is expressed as a distance in the volume's own space, where the shape occupies the
// unit region. That is what lets one feather, one bounds test and one density function serve all
// four: a volume's rotation and non-uniform scale are entirely the matrix's business, so a box can
// lean and a cylinder can tip without a single special case in here.

float FogShapeDistance(int kind, float3 p)
{
    if (kind == FOG_SHAPE_ELLIPSOID)
        return length(p);

    if (kind == FOG_SHAPE_CYLINDER)
        return max(length(p.xz), abs(p.y));

    if (kind == FOG_SHAPE_GROUND)
        return max(abs(p.x), abs(p.z));   // unbounded upward; the vertical profile ends it

    return max(abs(p.x), max(abs(p.y), abs(p.z)));
}

/// How the volume thins with height, in its own space where the floor is y = -1 and the top y = +1.
///
/// Air with anything suspended in it is heavier at the bottom, and a volume with a uniform vertical
/// profile reads as a solid object rather than as something settling. A ground layer takes this
/// further: it decays exponentially and is never clipped by a top face at all, which is the only
/// way to get low mist without a visible ceiling hanging over the player's head.
float FogVerticalProfile(int kind, float3 p, float falloff)
{
    float heightFraction = saturate((p.y + 1.0) * 0.5);

    if (kind == FOG_SHAPE_GROUND)
        return exp(-heightFraction * (0.5 + falloff * 6.0));

    return saturate(1.0 - falloff * heightFraction);
}

/// The ray interval that could possibly contain this volume, in world-space t.
///
/// The ray is carried into local space WITHOUT renormalising the direction, so t keeps its
/// world-space meaning through the transform and the caller never has to convert anything back.
bool FogVolumeBounds(int index, float3 ro, float3 rd, out float tNear, out float tFar)
{
    float4x4 toLocal = _FogWorldToLocal[index];
    float3 lo = mul(toLocal, float4(ro, 1.0)).xyz;
    float3 ld = mul((float3x3)toLocal, rd);

    int kind = (int)_FogShape[index].x;

    // The bound has to clear the feather, and then some: the noise pushes billows outside the
    // analytic surface, and a bound that stops at the surface shears them off along a hard edge.
    float outer = 1.0 + _FogShape[index].y + 0.15;

    if (kind == FOG_SHAPE_ELLIPSOID)
        return VolRaySphere(lo, ld, 0.0, outer, tNear, tFar);

    if (kind == FOG_SHAPE_CYLINDER)
        return VolRayCylinderY(lo, ld, outer, -outer, outer, tNear, tFar);

    return VolRayBox(lo, ld, -outer, outer, tNear, tFar);
}

/// Density of one volume at a world position. Zero outside it, which is the early-out that makes
/// evaluating eight volumes per sample affordable — a volume the ray is not inside costs a matrix
/// multiply and a compare, and never touches the noise texture.
float FogVolumeDensity(int index, float3 positionWS)
{
    float4x4 toLocal = _FogWorldToLocal[index];
    float3 p = mul(toLocal, float4(positionWS, 1.0)).xyz;

    int kind = (int)_FogShape[index].x;
    float feather = max(0.001, _FogShape[index].y);

    float shapeDistance = FogShapeDistance(kind, p);
    float coverage = 1.0 - smoothstep(1.0 - feather, 1.0 + feather, shapeDistance);
    if (coverage <= 0.002)
        return 0.0;

    coverage *= FogVerticalProfile(kind, p, _FogShape[index].z);
    if (coverage <= 0.002)
        return 0.0;

    float4 detailParams = _FogNoise[index];
    float noiseScale = max(1.0, detailParams.x);

    // Squashing the sample vertically stretches the billows horizontally, which is what moving air
    // does to anything suspended in it. Without it the noise is isotropic and the fog reads as
    // static smoke rather than as air with a direction.
    float3 samplePosition = float3(positionWS.x, positionWS.y * detailParams.z, positionWS.z)
                          + _FogMotion[index].xyz;
    samplePosition += VolBreathe(positionWS, _FogTime * _FogBreathe[index].z,
                                 _FogBreathe[index].x, _FogBreathe[index].y);

    float3 uvw = samplePosition / noiseScale;

    float4 base = SAMPLE_TEXTURE3D_LOD(_FogNoiseTex, sampler_FogNoiseTex, uvw, 0);
    float lowFrequency = saturate(base.r * 0.6 + base.g * 0.3 + base.b * 0.1);

    float density = VolCoverageRemap(lowFrequency, coverage);
    if (density <= 0.002)
        return 0.0;

    // The detail octave is sampled at a shifted offset so it does not correlate with the base and
    // simply deepen the same lumps.
    float4 fine = SAMPLE_TEXTURE3D_LOD(_FogNoiseTex, sampler_FogNoiseTex,
                                       uvw * max(1.5, detailParams.w) + 0.37, 0);
    float detail = saturate(fine.g * 0.6 + fine.b * 0.3 + fine.a * 0.1);

    density = VolErode(density, detail, detailParams.y);
    return density * _FogColor[index].a;
}

/// Total density of every volume along a short ray toward a light, as an optical depth.
float FogOpticalDepth(float3 origin, float3 direction, float distance, int steps)
{
    float stepLength = distance / max(1, steps);
    float optical = 0.0;
    float3 p = origin + direction * (stepLength * 0.5);

    for (int marchStep = 0; marchStep < steps; marchStep++)
    {
        [loop]
        for (int i = 0; i < _FogVolumeCount; i++)
            optical += FogVolumeDensity(i, p) * _FogShape[i].w;

        p += direction * stepLength;
    }

    return optical * stepLength;
}

/// What one sample of air looks like: the blended albedo, emission and behaviour of every volume
/// present there, weighted by how much of each is actually in this spot.
struct FogSample
{
    float density;
    float3 albedo;
    float3 emission;
    float extinction;
    float anisotropy;
    float ambient;
};

FogSample FogSampleAt(float3 positionWS)
{
    FogSample result;
    result.density = 0.0;
    result.albedo = 0.0;
    result.emission = 0.0;
    result.extinction = 0.0;
    result.anisotropy = 0.0;
    result.ambient = 0.0;

    [loop]
    for (int i = 0; i < _FogVolumeCount; i++)
    {
        float density = FogVolumeDensity(i, positionWS);
        if (density <= 0.002)
            continue;

        result.density += density;
        result.albedo += density * _FogColor[i].rgb;
        result.emission += density * _FogEmission[i].rgb;
        result.extinction += density * _FogShape[i].w;
        result.anisotropy += density * _FogMotion[i].w;
        result.ambient += density * _FogEmission[i].a;
    }

    if (result.density > 0.0)
    {
        // Weighted MEAN, not sum: the look parameters are properties of the air, so two overlapping
        // fogs give air that is half one and half the other. Density itself stays a sum, because two
        // overlapping fogs genuinely are thicker than either alone.
        float inverse = 1.0 / result.density;
        result.albedo *= inverse;
        result.emission *= inverse;
        result.extinction *= inverse;
        result.anisotropy *= inverse;
        result.ambient *= inverse;
    }

    return result;
}

/// Light reaching a point from the lamps in the scene.
///
/// No shadow march: a point light inside fog is almost always close enough that the fog between it
/// and the sample is a metre or two, and paying a second march per light per sample to model that
/// would cost more than everything else in this shader combined. What sells a lamp in mist is the
/// falloff and the forward scattering, and both are here.
float3 FogLocalLights(float3 positionWS, float3 rd, float anisotropy)
{
    float3 total = 0.0;

    [loop]
    for (int i = 0; i < _FogLightCount; i++)
    {
        float3 toLight = _FogLightPosition[i].xyz - positionWS;
        float distanceSquared = dot(toLight, toLight);

        // URP's own inverse-square-with-smooth-cutoff, so a lamp lights the fog over the same
        // distance it lights the walls. A bare inverse square never reaches zero and every lamp
        // ends up tinting the whole volume faintly.
        //
        // The one deviation is the +1 in the denominator. URP divides by the squared distance
        // alone, which is fine for a surface — no surface is ever AT the light. A fog sample can
        // land exactly on a lamp, and there the bare reciprocal is a division by zero that shows up
        // as a single blown-out pixel flickering inside every lamp in the scene.
        float attenuation = rcp(distanceSquared + 1.0);
        float factor = distanceSquared * _FogLightPosition[i].w;
        float smoothCutoff = saturate(1.0 - factor * factor);
        attenuation *= smoothCutoff * smoothCutoff;

        if (attenuation <= 0.0001)
            continue;

        float3 lightDirection = toLight * rsqrt(max(distanceSquared, 1e-6));
        float phase = VolPhaseDual(abs(anisotropy) * 0.6, 0.2, 0.3, dot(rd, lightDirection));

        total += _FogLightColor[i].rgb * attenuation * phase;
    }

    return total;
}

/// Marches every volume at once and returns scattered colour in rgb, coverage in a.
float4 FogRaymarch(float3 ro, float3 rd, float tNear, float tFar, float smallestFeature,
                   float3 sunDirection, float3 sunColor, float jitter)
{
    float span = tFar - tNear;
    if (span <= 0.0)
        return 0.0;

    int steps = max(4, (int)_FogSteps);

    // Dividing the whole span by the step count is what turns a deep volume into a flat gradient:
    // the steps come out longer than the billows and march straight past them. Cap the step at a
    // fraction of the smallest billow in play instead, and accept not reaching the far side — by
    // then the air is opaque and there is nothing back there to see.
    //
    // A quarter, not a half: the per-pixel jitter offsets the first sample by up to a full step, so
    // a long step turns into visible crosshatch stipple across the whole volume.
    float stepLength = min(span / steps, smallestFeature * 0.25);
    float t = tNear + stepLength * jitter;

    int lightSteps = max(1, (int)_FogLightSteps);
    float lightDistance = smallestFeature * 1.5;

    float transmittance = 1.0;
    float3 scatter = 0.0;

    [loop]
    for (int i = 0; i < steps; i++)
    {
        float3 p = ro + rd * t;
        t += stepLength;

        FogSample air = FogSampleAt(p);
        if (air.density <= 0.002)
            continue;

        float phase = VolPhaseDual(air.anisotropy, 0.25, 0.25, dot(rd, sunDirection));
        float powder = VolPowder(air.density, 6.0);

        float optical = FogOpticalDepth(p, sunDirection, lightDistance, lightSteps);
        float sun = VolMultiScatter(optical);

        float3 luminance = air.albedo * air.ambient * _FogSkyLight.rgb
                         + air.albedo * sunColor * sun * phase * powder
                         + air.albedo * FogLocalLights(p, rd, air.anisotropy)
                         + air.emission;

        float absorbed = 1.0 - exp(-air.density * air.extinction * stepLength);
        scatter += transmittance * absorbed * luminance;
        transmittance *= 1.0 - absorbed;

        if (transmittance < 0.01)
            break;
    }

    float alpha = saturate(1.0 - transmittance);

    // Un-premultiply, so the result composites with ordinary source-alpha blending.
    return float4(scatter / max(alpha, 1e-4), alpha);
}

#endif
