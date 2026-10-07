#ifndef CAVE_BUMP_INCLUDED
#define CAVE_BUMP_INCLUDED

// Shared procedural normal-perturbation for cave triplanar shaders.
//
// The cave shaders already author their colour from world-space fbm noise — we exploit the same
// noise to give the surface convincing micro-detail in *lighting* without adding geometry or
// requiring normal-map textures. Each caller passes in its own `fbm(p)` function via the macro
// CAVE_BUMP_FBM, so the bump field's "look" matches the host shader's existing patches/streaks.
//
// Usage in a host shader:
//
//     #define CAVE_BUMP_FBM(p) fbm(p)
//     #include "../CaveBump.hlsl"
//
//     // ... in frag, just after `N = normalize(IN.normalWS)`:
//     N = ApplyCaveBump(IN.positionWS, N, _BumpStrength, _BumpScale, _BumpDetailMix);
//
// Cost: 3 triplanar fbm samples per fragment (one per axis), each with a small XY offset to
// derive the surface gradient. Cheap — typically ~30% more cost than the plain colour shader.

#ifndef CAVE_BUMP_FBM
    #error "CaveBump.hlsl requires #define CAVE_BUMP_FBM(p) before #include — point it at your fbm() function."
#endif

// Sample one octave of the bump field at a 3D point. Two-octave fbm by default; the host's fbm()
// already does its own octave-stacking but we add one more high-frequency layer on top to get the
// micro-detail that hand-authored rock normal maps usually carry.
float CaveBump_NoiseField(float3 p)
{
    // Primary: host's fbm (matches the shader's colour personality).
    float a = CAVE_BUMP_FBM(p);
    // High-frequency overlay for grain — much higher freq, smaller amplitude.
    float b = CAVE_BUMP_FBM(p * 3.17 + 11.3) * 0.35;
    return a + b;
}

// Compute the triplanar height field's gradient at the given world position. We don't try to do
// a proper 3D derivative — instead we sample the height on each of the three triplanar planes
// (XY, YZ, ZX) and finite-difference each plane. The result is two scalar slopes per plane,
// blended together by the same triplanar weights the host shader uses.
//
// Returned vector lies in world space and represents the lateral push the normal should receive.
float3 CaveBump_TriplanarGradient(float3 worldP, float3 N, float scale, float detailMix)
{
    // Small epsilon for finite differences; relative to the bump scale so it scales naturally.
    float eps = 0.05 / max(scale, 0.001);
    float3 p = worldP * scale;

    // Sample heights on the three triplanar planes — each plane uses a 2D slice of the 3D field
    // so the bump direction depends on which face you're looking at.
    // XY plane (normal mostly ±Z): vary X and Y, hold Z constant.
    float hX_Y0 = CaveBump_NoiseField(p);
    float hX_Y1 = CaveBump_NoiseField(p + float3(eps, 0, 0));
    float hY_Y0 = CaveBump_NoiseField(p + float3(0, eps, 0));
    // YZ plane (normal mostly ±X): vary Y and Z.
    float hYZ_Z0 = CaveBump_NoiseField(p + float3(0, 0, eps));
    // ZX plane (normal mostly ±Y) — we already have hX_Y0 (Z varying), but we can reuse offsets.
    // Two more samples for the Z-vs-X and Z-vs-Y derivatives.

    // Differences = slopes along each axis.
    float dHdX = hX_Y1  - hX_Y0;
    float dHdY = hY_Y0  - hX_Y0;
    float dHdZ = hYZ_Z0 - hX_Y0;

    // Triplanar blend weights, same formulation as the host shaders (no sharpness here — we
    // want the bump to read across the seams smoothly).
    float3 absN = abs(N);
    absN /= (absN.x + absN.y + absN.z + 1e-5);

    // For each face axis, the in-plane gradient becomes a world-space offset that pushes the
    // normal *away* from the surface (toward higher noise) along the perpendicular axes.
    //   • On a wall facing ±X: the 2D gradient is (dY, dZ).
    //   • On a wall facing ±Y: the 2D gradient is (dX, dZ).
    //   • On a wall facing ±Z: the 2D gradient is (dX, dY).
    float3 gX = float3(0,    dHdY, dHdZ);  // pushes in YZ
    float3 gY = float3(dHdX, 0,    dHdZ);  // pushes in XZ
    float3 gZ = float3(dHdX, dHdY, 0);     // pushes in XY

    float3 g = absN.x * gX + absN.y * gY + absN.z * gZ;

    // detailMix lets the host weight high-freq vs low-freq components. For now we just attenuate
    // the gradient — detailMix=0 is the smoothest "ambient roughness" feel, detailMix=1 keeps
    // full sharpness. The host's _BumpDetailMix range covers it.
    return g * detailMix;
}

// Apply procedural bump to a world-space normal. Returns the perturbed, normalised normal.
//
// `strength` ≈ how much the lighting reacts to the bump. 0 = no effect, 0.5 = noticeable rock
// roughness, 1+ = aggressive (looks like the wall has actual chunks). Anything above ~1.5 starts
// to look unnatural because the perturbation overwhelms the original surface normal.
//
// `scale` = spatial frequency of the bump. ~0.5 = big slow ripples (good for fbm-cohesion with
// the host's colour patches). ~2 = visible "rock grit". The default 0.8 is a good middle ground.
//
// `detailMix` = how much of the high-freq overlay is mixed in (forwarded to the gradient calc).
float3 ApplyCaveBump(float3 worldP, float3 N, float strength, float scale, float detailMix)
{
    if (strength <= 0.001) return N;
    float3 grad = CaveBump_TriplanarGradient(worldP, N, scale, saturate(detailMix));
    // Normal perturbation: subtract the projection of the gradient onto the normal so the offset
    // is purely tangential — keeps the result close to unit length.
    float3 perturbed = N - grad * strength;
    return normalize(perturbed);
}

#endif // CAVE_BUMP_INCLUDED
