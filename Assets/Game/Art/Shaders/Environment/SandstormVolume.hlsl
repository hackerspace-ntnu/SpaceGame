#ifndef SANDSTORM_VOLUME_INCLUDED
#define SANDSTORM_VOLUME_INCLUDED

// The storm as an actual volume of sand, rather than a painted surface.
//
// This is the Nubis recipe at the scale a desert game can afford: a tiling Perlin-Worley volume
// erodes an analytic shape into billows, Beer-Lambert integrates the result along the view ray, a
// four-step march toward the sun supplies self-shadowing, and a Henyey-Greenstein lobe makes the
// storm glow when you look through it toward the light. The first version of this shader had none
// of that and looked exactly like what it was: a brown cylinder.
//
// The shape function here must stay identical to StormShape.Density in C#. The eroding noise is
// deliberately NOT mirrored on the CPU — it only breaks up the edges, so the storm that hurts you
// and the storm you can see agree on where they are to within a wisp.

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

TEXTURE3D(_SandstormNoise);
SAMPLER(sampler_SandstormNoise);

// The sky's own radiance, in render space, pushed here by SandstormVisuals.PushSkyLight.
//
// It is handed in rather than read because NEITHER path this file serves can read it itself:
// SampleSH() reads the per-draw SH constants, and the fullscreen fog is a procedural blit that
// has none, while the silhouette's renderer has light probes switched off. It returned zero in
// both — and inside a storm, where the sun march is fully occluded in every direction, this is
// the ONLY light there is, so the interior came out as a flat black-brown screen.
float4 _SandstormSkyLight;

// One storm's geometry. Filled from a MaterialPropertyBlock by the shell, or from globals by the
// fullscreen pass, so both render the same storm the same way.
struct StormVolume
{
    float3 center;          // xz = centre, y = base height
    float2 heading;         // unit, direction of travel and the wall's normal
    float  radius;          // cell: core radius. wall: half-thickness
    float  lateralExtent;   // wall only, <= 0 means unbounded
    float  edgeFeather;
    float  height;
    float  heightFeather;
    float  isWall;
    float  intensity;       // lifecycle 0..1
};

struct StormLook
{
    float3 color;
    float  extinction;      // per metre at full density
    float  noiseScale;      // metres per tile of the base noise
    float3 drift;           // metres the noise has scrolled
    float  erosion;         // 0..1, how hard the detail carves the edges
    float  anisotropy;      // Henyey-Greenstein g
    float  ambient;         // fraction of the sand lit with no sun contribution
    float  stretch;         // how much the billows are drawn out horizontally by the wind
    int    steps;
    int    lightSteps;
};

float StormFalloff(float distance, float core, float feather)
{
    return 1.0 - smoothstep(core, core + feather, distance);
}

// Mirrors StormShape.Density exactly. Everything else in this file only decorates it.
float StormShapeAt(StormVolume v, float3 p)
{
    float2 relative = p.xz - v.center.xz;
    float horizontal;

    if (v.isWall > 0.5)
    {
        float2 heading = normalize(v.heading);
        float2 across = float2(-heading.y, heading.x);

        horizontal = StormFalloff(abs(dot(relative, heading)), v.radius, v.edgeFeather);
        if (v.lateralExtent > 0.0)
            horizontal *= StormFalloff(abs(dot(relative, across)), v.lateralExtent, v.edgeFeather);
    }
    else
    {
        horizontal = StormFalloff(length(relative), v.radius, v.edgeFeather);
    }

    // Sand fills holes: below the base is as thick as inside.
    float above = p.y - v.center.y;
    float vertical = above <= 0.0
        ? 1.0
        : StormFalloff(above, v.height - v.heightFeather, v.heightFeather);

    return horizontal * vertical * v.intensity;
}

// Density.
//
// The analytic shape is only a COVERAGE mask here, never the density itself. That distinction is
// the whole difference between a storm and a box: if the shape is used directly, everything inside
// the feather saturates to opaque and the silhouette becomes the bounding volume with a fuzzy rim.
// Instead the low-frequency noise is remapped against coverage, so the noise decides the shape and
// coverage only decides how much of the noise survives — full inside the core, only the peaks at
// the edges. It is the standard cloud formulation, and it is what makes the mass lumpy rather than
// extruded.
float StormDensityAt(StormVolume v, StormLook look, float3 p)
{
    float coverage = StormShapeAt(v, p);
    if (coverage <= 0.001)
        return 0.0;

    float heightFraction = saturate((p.y - v.center.y) / max(1.0, v.height));

    // Squashing the sample vertically stretches the billows horizontally, which is what wind does
    // to dust. Without it the noise is isotropic and the storm reads as smoke sitting still.
    float3 warped = float3(p.x, p.y * look.stretch, p.z);
    float3 uvw = (warped + look.drift) / look.noiseScale;

    float4 base = SAMPLE_TEXTURE3D_LOD(_SandstormNoise, sampler_SandstormNoise, uvw, 0);
    float lowFrequency = saturate(base.r * 0.6 + base.g * 0.3 + base.b * 0.1);

    float density = saturate((lowFrequency - (1.0 - coverage)) / max(0.001, coverage));
    if (density <= 0.001)
        return 0.0;

    // Then, and only then, the detail octave carves the result. Sampled at a shifted offset so it
    // does not correlate with the base and reinforce the same lumps.
    float4 fine = SAMPLE_TEXTURE3D_LOD(_SandstormNoise, sampler_SandstormNoise, uvw * 4.1 + 0.37, 0);
    float detail = saturate(fine.g * 0.6 + fine.b * 0.3 + fine.a * 0.1);

    float carve = (1.0 - detail) * look.erosion;
    density = saturate((density - carve) / max(0.001, 1.0 - carve));

    // Sand is not a cloud: it is lifted off the ground, so a storm is heavier at the bottom and
    // wisps at the top. Applied to the density AFTER the remap and never to the coverage — scaling
    // coverage would push the visible edge further out than the shape the CPU is doing damage
    // with, and the player would take no damage in sand they can plainly see.
    //
    // Kept deliberately mild. A strong boost makes DOWNWARD the most opaque direction there is,
    // which takes away the one piece of the world a player inside a storm needs to keep: the
    // ground under their own feet.
    float groundWeight = saturate(1.0 - heightFraction * 2.5);
    return saturate(density * (1.0 + 0.35 * groundWeight));
}

// Henyey-Greenstein, normalised so isotropic scattering comes out at 1 and the artist parameter
// reads as "how much brighter is this when the sun is behind it". Forward scattering is most of
// why dust looks like dust: a storm between you and the sun lights up, and the same storm with the
// sun behind you is a dull brown wall.
float StormPhase(float g, float cosAngle)
{
    float gg = g * g;
    return (1.0 - gg) / pow(abs(1.0 + gg - 2.0 * g * cosAngle), 1.5);
}

// How much sun reaches a point inside the storm.
//
// A single Beer-Lambert term is wrong here in a way that is immediately visible: half a kilometre
// of dust has an optical depth in the hundreds, so every pixel comes back pure black and the storm
// renders as a hole in the world. Real dust is bright precisely BECAUSE it is dense — light that
// cannot pass straight through bounces instead.
//
// This is the standard cheap stand-in for that: sum a few Beer terms with geometrically decreasing
// extinction, so the first octave is the direct beam and the later ones stand in for light that
// took a longer, scattered route. Three octaves is enough to turn the black hole into a lit body.
float StormLightMarch(StormVolume v, StormLook look, float3 p, float3 sunDirection)
{
    // Stepped against the storm's own height rather than the noise scale. The noise is authored at
    // storm scale now, so a step derived from it overshoots the whole volume in one hop and every
    // point comes back equally lit — which reads as no shading at all.
    float stepLength = v.height * 0.05;
    float optical = 0.0;
    float3 q = p;

    for (int i = 0; i < look.lightSteps; i++)
    {
        // Growing steps: the near samples decide the shading, the far ones only need to notice
        // that a kilometre of storm is in the way.
        stepLength *= 1.6;
        q += sunDirection * stepLength;
        optical += StormDensityAt(v, look, q) * stepLength;
    }

    float depth = optical * look.extinction;
    float transmittance = 0.0;
    float weight = 1.0;
    float scale = 1.0;

    for (int octave = 0; octave < 3; octave++)
    {
        transmittance += weight * exp(-depth * scale);
        weight *= 0.55;
        scale *= 0.28;
    }

    return transmittance;
}

// Clips a ray interval against one pair of parallel planes, given the plane normal in 2D. Used for
// a wall's thickness and its lateral ends; a cell needs neither.
bool StormSlab(float2 origin, float2 direction, float2 normal, float halfWidth,
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

// Ray against the storm's bounding shape. Returns false when the ray misses it entirely, so a
// pixel that has nothing to march can leave immediately.
bool StormRayBounds(StormVolume v, float3 ro, float3 rd, out float tNear, out float tFar)
{
    tNear = 0.0;
    tFar = 1e9;

    float outerRadius = v.radius + v.edgeFeather;

    // Vertical slab. The floor sits well below the base because density does not stop there — the
    // storm fills whatever hole the terrain has dug.
    float top = v.center.y + v.height;
    float bottom = v.center.y - 400.0;
    if (abs(rd.y) > 1e-5)
    {
        float t0 = (bottom - ro.y) / rd.y;
        float t1 = (top - ro.y) / rd.y;
        tNear = max(tNear, min(t0, t1));
        tFar = min(tFar, max(t0, t1));
    }
    else if (ro.y < bottom || ro.y > top)
    {
        return false;
    }

    if (v.isWall > 0.5)
    {
        float2 heading = normalize(v.heading);
        float2 across = float2(-heading.y, heading.x);

        if (!StormSlab(ro.xz - v.center.xz, rd.xz, heading, outerRadius, tNear, tFar))
            return false;

        if (v.lateralExtent > 0.0 &&
            !StormSlab(ro.xz - v.center.xz, rd.xz, across, v.lateralExtent + v.edgeFeather, tNear, tFar))
            return false;
    }
    else
    {
        // Infinite cylinder about Y; the vertical slab above already capped it.
        float2 oc = ro.xz - v.center.xz;
        float a = dot(rd.xz, rd.xz);
        float b = 2.0 * dot(oc, rd.xz);
        float c = dot(oc, oc) - outerRadius * outerRadius;

        if (a < 1e-6)
        {
            if (c > 0.0)
                return false;   // straight up or down, and outside the circle
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
    }

    tNear = max(tNear, 0.0);
    return tFar > tNear;
}

// Marches the storm and returns scattered colour in rgb and coverage in a.
float4 StormRaymarch(StormVolume v, StormLook look, float3 ro, float3 rd,
                     float tNear, float tFar, float3 sunDirection, float3 sunColor, float jitter)
{
    float span = tFar - tNear;
    if (span <= 0.0)
        return 0.0;

    // Dividing the whole span by the step count is what turns a kilometre-deep storm into a flat
    // gradient: the steps come out longer than the billows and march straight past them. Cap the
    // step at half a billow instead, and accept not reaching the far side — by then the sand is
    // opaque to six decimal places and there is nothing back there to see.
    // A quarter of a billow, not half: the per-pixel jitter below offsets the first sample by up to
    // a full step, so a long step turns into visible crosshatch stipple across the whole storm.
    float featureSize = look.noiseScale / max(1.0, look.stretch);
    float stepLength = min(span / look.steps, featureSize * 0.25);
    float t = tNear + stepLength * jitter;

    float phase = StormPhase(look.anisotropy, dot(rd, sunDirection));
    float transmittance = 1.0;
    float3 scatter = 0.0;

    for (int i = 0; i < look.steps; i++)
    {
        float3 p = ro + rd * t;
        t += stepLength;

        float density = StormDensityAt(v, look, p);
        if (density > 0.002)
        {
            float sun = StormLightMarch(v, look, p, sunDirection);

            // The powder term: without it the side of a billow facing the sun reads as flat and
            // washed out, because a single scattering model has no idea light has to get INTO the
            // dust before it can come back out.
            float powder = 1.0 - exp(-density * 6.0);

            // Sky light falls on the storm from above, so the top of a column is lit and the base
            // is in its own shadow. Without this the whole body shades uniformly and a
            // kilometre-tall storm reads as flat as a painted backdrop.
            //
            // The gradient BRIGHTENS upward from a floor of one; it does not darken downward. That
            // distinction is the whole difference between a lit interior and a dark one, because
            // heightFraction is measured from the storm's BASE and a player standing inside a storm
            // is always at the base — hf never leaves 0.02, so a floor below one is a permanent tax
            // on the exact view this term exists to protect. At 0.55 it cost the interior 45% of
            // its light and the sand landed at half the green of the authored swatch.
            //
            // Lit by what actually falls on the TOP of the storm: the sky, plus the sun weighted
            // by how high it is. Both halves matter and both were wrong.
            //
            // The sky half was read with SampleSH and came back zero (see _SandstormSkyLight).
            // The sun half was a flat 0.35 of the sun colour, which measures nothing — it gave a
            // storm at noon exactly the light of one at sunset. Together they left the interior at
            // linear (0.13, 0.03, 0.004) with this profile: five percent luminance, and a saturated
            // ochre loses nearly all its green and blue in linear space, so what reached the screen
            // was black with a red cast. Using the sun's elevation instead is both a real quantity
            // and the day-night response the storm was supposed to have.
            float heightFraction = saturate((p.y - v.center.y) / max(1.0, v.height));
            float3 ambientSky = _SandstormSkyLight.rgb + sunColor * saturate(sunDirection.y);
            // With the floor at one, `fogColor` finally means what the profile says it means —
            // "the colour the air takes on inside the storm" — and `ambient` is a dial around it.
            float3 skyLight = look.color * look.ambient * (1.0 + 0.45 * heightFraction) * ambientSky;

            float3 luminance = skyLight + look.color * sunColor * sun * phase * powder;

            float absorbed = 1.0 - exp(-density * look.extinction * stepLength);
            scatter += transmittance * absorbed * luminance;
            transmittance *= 1.0 - absorbed;

            if (transmittance < 0.01)
                break;
        }
    }

    float alpha = saturate(1.0 - transmittance);
    // Un-premultiply, so the result blends correctly with ordinary source-alpha blending.
    return float4(scatter / max(alpha, 1e-4), alpha);
}

#endif
