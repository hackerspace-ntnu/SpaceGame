#ifndef SPACEGAME_STYLIZEDWATERNOISE_INCLUDED
#define SPACEGAME_STYLIZEDWATERNOISE_INCLUDED

// The patterns StylizedWater draws with, read from the textures WaterNoiseBaker bakes
// (SpaceGame > Environment > Bake Water Noise). Evaluating them per pixel cost ~5.5 ms full
// screen at 1080p; fetches cost almost nothing.
//
// _WaterNoise is a volume whose third axis is time, so moving through it makes a pattern change
// shape rather than slide — the thing that made texture-panned water read as fake. It wraps.
//   r   gradient noise A, about -0.7..0.7     gb  A's slope along x and y, per cell
//   a   gradient noise B, independent of A
// _WaterCaustics is a static, sharp Voronoi edge-distance map; the caustics animate it by
// warping two layers of it with noise A.
//
// Positions are given in cells; these must match the constants in WaterNoiseBaker.

#define WATER_NOISE_CELLS 8.0
#define WATER_CAUSTIC_CELLS 4.0
#define WATER_CAUSTIC_RANGE 1.5

TEXTURE3D(_WaterNoise);
SAMPLER(sampler_WaterNoise);
TEXTURE2D(_WaterCaustics);
SAMPLER(sampler_WaterCaustics);

// Explicit LOD: the volume has no mips, and LOD 0 keeps the fetch legal in the vertex stage.
half4 WaterNoise(float3 cells)
{
    return SAMPLE_TEXTURE3D_LOD(_WaterNoise, sampler_WaterNoise, cells / WATER_NOISE_CELLS, 0);
}

half WaterNoise01(float3 cells)
{
    return saturate(WaterNoise(cells).r * 0.75 + 0.5);
}

// The slope (dh/dx, dh/dz) of one ripple octave. The pattern drifts along `drift` while it
// moves through the volume's time axis at `change`, so it travels and re-forms at once.
float2 WaterRippleOctave(float2 xz, float scale, float2 drift, float change, float time)
{
    return WaterNoise(float3(xz / scale + drift * time, time * change)).gb / scale;
}

// The ripples' slope at full strength: two octaves, a coarse one and a finer one crossing it.
// Shared by the surface seen from above and from below, so both show the same ripples.
float2 WaterRippleSlope(float2 xz, float scale, float drift, float change, float time)
{
    return WaterRippleOctave(xz, scale, float2(0.7, 0.5) * drift, change, time)
         + WaterRippleOctave(xz + 5.3, scale * 0.37, float2(-0.4, 0.9) * drift, change, time * 1.3) * 0.5;
}

// Distance to the nearest edge of the caustic web, in cells. Mipmapped, so it must be called
// outside branches.
half WaterCausticEdge(float2 cells)
{
    return SAMPLE_TEXTURE2D(_WaterCaustics, sampler_WaterCaustics, cells / WATER_CAUSTIC_CELLS).r * WATER_CAUSTIC_RANGE;
}

#endif
