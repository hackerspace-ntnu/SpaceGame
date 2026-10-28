#ifndef SPACEGAME_ROCKSHARED_INCLUDED
#define SPACEGAME_ROCKSHARED_INCLUDED

// Shared by RockCull.compute, which culls every rock once per camera, and StylizedRock.shader,
// which draws the rocks it kept.

// One rock that survived culling. Written by RockCull.compute, read by every pass of
// StylizedRock.shader; change both together (and VisibleStride in RockClassStream.cs).
struct RockVisible
{
    float4 positionScale;   // xyz mesh origin in world space, w uniform scale (shrinks to 0 at the cut-off)
    float4 rotation;        // quaternion
    float4 stretchTint;     // xyz per-axis stretch, w tint 0..1
};

float3 RotateByQuaternion(float4 q, float3 v)
{
    float3 t = 2.0 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

#endif
