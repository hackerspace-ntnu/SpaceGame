#ifndef SPACEGAME_GRASSSHARED_INCLUDED
#define SPACEGAME_GRASSSHARED_INCLUDED

// Shared by GrassCull.compute, which culls and animates every tuft once per camera, and
// StylizedGrass.shader, which draws the tufts it kept.

// One tuft that survived culling, already animated for this frame. Written by
// GrassCull.compute, read by every pass of StylizedGrass.shader; change both together.
struct GrassVisible
{
    float4 rootHeight;   // xyz root in world space, w tuft height in metres
    float4 turnSway;     // x cos(yaw), y sin(yaw), zw tip sway per metre of blade (world XZ)
    float4 look;         // x tint, y patch mask | ground contact (GrassPackHalves), z variation, w flower hue (0 = no flower)
    float4 motion;       // x gust 0..1, y flatten 0..1, z standing 1 .. sunk 0, w width scale (>= 1 when thinned)
};

// Two 0..1 values in one float, as two halves (low, high), so a value can ride along without
// growing GrassInstance or GrassVisible. GrassInstance.cs packs the same way on the CPU.
float GrassPackHalves(float low, float high)
{
    return asfloat(f32tof16(low) | (f32tof16(high) << 16));
}

float2 GrassUnpackHalves(float packed)
{
    uint bits = asuint(packed);
    return float2(f16tof32(bits), f16tof32(bits >> 16));
}

// The range a tuft's detail level is chosen by: its distance, jittered ±10 % by its tint so the
// switch is a scatter, not a ring. GrassCull.compute picks the level by it and the grass shader
// morphs toward the next level by it; the two must agree, or a tuft is swapped mid-morph.
float GrassLodRange(float3 root, float3 eye, float tint)
{
    return length(root - eye) * (0.9 + 0.2 * frac(tint * 7.13));
}

// The (cos, sin) turn that faces a distant tuft's spikes, which lie in their local XY plane
// facing +Z, toward the eye (the yaw rotation maps local Z to (-sin, cos) in world XZ).
float2 GrassFaceEye(float3 root, float3 eye)
{
    float2 toEye = normalize(eye.xz - root.xz + float2(1e-4, 0));
    return float2(toEye.y, -toEye.x);
}

float GrassHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
}

float GrassNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = GrassHash(i);
    float b = GrassHash(i + float2(1, 0));
    float c = GrassHash(i + float2(0, 1));
    float d = GrassHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// How hard a gust is blowing at a point (0..1). Two octaves drift downwind at different speeds
// (gustSpeed in m/s) and slide apart sideways, so a gust swells, wanders and dies as they pass
// through each other instead of sliding across the field as one fixed stamp.
float WindGust(float2 positionXZ, float2 windDir, float time, float gustFrequency, float gustSpeed)
{
    float2 across = float2(windDir.y, -windDir.x);
    float travelled = time * gustSpeed;
    float broad = GrassNoise((positionXZ - windDir * (travelled * 0.8) - across * (travelled * 0.25))
                             * (gustFrequency * 0.6));
    float fine = GrassNoise((positionXZ - windDir * (travelled * 1.3) + across * (travelled * 0.2))
                            * (gustFrequency * 1.7) + 17.3);
    return smoothstep(0.2, 0.85, broad * 0.65 + fine * 0.35);
}

// The wind's push at a point (0 .. ~1.4): waves rolling along the wind direction, made uneven
// by gusts, so neighbours move together — which reads as flowing rather than shivering. The
// crests are bent by a slowly drifting warp and crossed by a second, shorter and slower train at
// an angle, so they arrive curved, reinforce and cancel, and the rhythm never repeats. Shared
// with StylizedFoliage so a tree leans on the same wave as the meadow around it.
float WindPush(float2 positionXZ, float2 windDir, float time, float waveLength, float waveSpeed,
               float gustFrequency, float gustSpeed, out float gust)
{
    waveLength = max(waveLength, 0.01);
    float2 across = float2(windDir.y, -windDir.x);
    float k = 6.2831 / waveLength;
    float warp = (GrassNoise((positionXZ - windDir * (time * waveSpeed * 0.5)) * (0.35 / waveLength) + 53.1)
                  - 0.5) * waveLength * 1.5;
    float primary = sin((dot(positionXZ, windDir) + warp - time * waveSpeed) * k);
    float2 crossDir = normalize(windDir + across * 0.55);
    float secondary = sin((dot(positionXZ, crossDir) - warp * 0.7 - time * waveSpeed * 0.73) * k * 1.61);
    float wave = saturate(0.5 + primary * 0.32 + secondary * 0.18);
    gust = WindGust(positionXZ, windDir, time, gustFrequency, gustSpeed);
    // A calm patch barely stirs; a gust drives the waves hard.
    return wave * (0.25 + gust * 1.15);
}

#endif
