#ifndef SPACEGAME_STYLIZEDWATER_INCLUDED
#define SPACEGAME_STYLIZEDWATER_INCLUDED

// The layers of StylizedWater.shader, in the order the fragment stacks them. Each one is a
// function of what lies under the surface and of time, so nothing on the water holds still.
//
//   Swells   - (vertex) three Gerstner waves crossing at angles, so the surface rolls without
//              ever repeating along one direction.
//   Ripples  - the surface normal: two octaves of the baked noise's slope, each drifting while
//              it moves through the volume's time axis, so ripples re-form as they travel.
//   Scene    - where the bed under this pixel is, read back from the camera depth texture;
//              read through the ripples, which is the refraction.
//   Body     - the water's own colour: shore -> shallow -> deep by how much water stands
//              over the bed, with a slow, faint drift (vertex) so large areas never sit at one tint.
//   See-through - the bed shows through, fading out as the light's path through the water
//              grows, so the shallows are clear and the middle is the body colour.
//   Caustics - a web of light on the bed: two layers of the baked Voronoi web drifting apart,
//              each warped differently by evolving noise, so where they cross it keeps
//              re-forming; strongest in the shallows, gone in the deep and wherever the bed is
//              in shadow.
//   Shade    - the body darkens in the sun's shadow (reeds, rocks, the shore), so the water
//              sits in the scene instead of glowing on top of it.
//   Reflection - the planar reflection the PlanarReflectionManager renders, bent by the
//              ripples, over a fresnel ramp: little straight down, mostly mirror at a glance.
//              Falls back to the reflection probe where no planar reflection is active.
//   Foam     - a rim that is solid at the waterline and frays outward; lines that travel in
//              toward the shore, thinning and breaking as they come; and thin streaks on the
//              open water in drifting patches (vertex). All of it is thresholded evolving noise,
//              so the foam changes shape rather than sliding.
//   Glints   - the sun caught on ripple faces, a soft-edged threshold of the highlight, so
//              the light breaks into moving streaks rather than one smooth hot spot.
//
// The surface is not drawn from below: under water, UnderwaterRenderFeature paints it, in
// colours taken from this material.
//
// Cost: per pixel, 5 noise-volume and 2 caustic fetches, 2 depth and 1 colour read, 1-2 hard
// shadow samples and one reflection read; per vertex, 2 noise-volume fetches. Soft shadows are deliberately not compiled in — the
// ripples hide the difference and the filter cost a millisecond full screen.

// Shadows on a transparent surface must come from the shadow map, never the screen-space
// shadow texture, which only holds the opaque scene.
#define _SURFACE_TYPE_TRANSPARENT 1
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#include "StylizedWaterNoise.hlsl"

CBUFFER_START(UnityPerMaterial)
    half4 _ShoreColor;
    half4 _ShallowColor;
    half4 _DeepColor;
    float _ShallowDepth;
    float _DeepDepth;
    float _Clarity;
    half  _MaxOpacity;
    half4 _BedTint;
    half  _DriftStrength;
    float _DriftScale;
    float _DriftSpeed;

    float4 _SwellA;          // xy direction, z steepness, w wavelength (m)
    float4 _SwellB;
    float4 _SwellC;
    float  _SwellSpeed;

    float  _RippleScale;
    half   _RippleStrength;
    float  _RippleDrift;
    float  _RippleChange;
    float  _RippleFadeDistance;

    half   _Refraction;

    half   _ShadowStrength;
    half4  _ShadowTint;

    half   _ReflectionStrength;
    half   _ReflectionMinimum;
    half   _FresnelPower;
    half   _ReflectionDistortion;
    half   _ProbeRoughness;
    half   _PlannerReflectionBlend;   // written by PlanarReflectionManager: 0 planar, 1 probe

    half4  _CausticColor;
    float  _CausticScale;
    float  _CausticSpeed;
    half   _CausticWarp;
    half   _CausticThickness;
    float  _CausticDepth;
    float  _CausticFadeDistance;

    half4  _GlintColor;
    half   _GlintPower;
    half   _GlintThreshold;
    half   _GlintSoftness;

    half4  _FoamColor;
    float  _FoamNoiseScale;
    float  _FoamChange;
    float  _RimWidth;
    float  _ShoreLineSpacing;
    float  _ShoreLineSpeed;
    half   _ShoreLineWidth;
    float  _ShoreLineReach;
    float  _StreakScale;
    float  _StreakDrift;
    half   _StreakWidth;
    half   _StreakCoverage;
CBUFFER_END

TEXTURE2D(_PlanarReflectionTexture);
SAMPLER(sampler_PlanarReflectionTexture);

// ---------------------------------------------------------------------------------------------
// Swells

// One Gerstner wave: moves the point in a circle and accumulates the surface's tangent and
// binormal, from which the vertex normal is rebuilt.
float3 GerstnerSwell(float4 swell, float3 positionWS, float time, inout float3 tangent, inout float3 binormal)
{
    float steepness = swell.z;
    float k = 6.2831853 / swell.w;
    float speed = sqrt(9.8 / k) * _SwellSpeed;
    float2 d = normalize(swell.xy);
    float f = k * (dot(d, positionWS.xz) - speed * time);
    float a = steepness / k;
    float s = steepness * sin(f);
    float c = steepness * cos(f);

    tangent += float3(-d.x * d.x * s, d.x * c, -d.x * d.y * s);
    binormal += float3(-d.x * d.y * s, d.y * c, -d.y * d.y * s);
    return float3(d.x * a * cos(f), a * sin(f), d.y * a * cos(f));
}

void ApplySwells(inout float3 positionWS, out float3 normalWS, float time)
{
    float3 tangent = float3(1, 0, 0);
    float3 binormal = float3(0, 0, 1);
    float3 rest = positionWS;
    positionWS += GerstnerSwell(_SwellA, rest, time, tangent, binormal);
    positionWS += GerstnerSwell(_SwellB, rest, time, tangent, binormal);
    positionWS += GerstnerSwell(_SwellC, rest, time, tangent, binormal);
    normalWS = normalize(cross(binormal, tangent));
}

// ---------------------------------------------------------------------------------------------
// Slow patterns, evaluated per vertex

// Patterns metres across change far slower than the 0.5 m surface grid, so they are sampled
// once per vertex and interpolated instead of once per pixel.
//   x  colour drift, -1..1        y  where open-water streaks may appear, 0..1
half2 SlowPatterns(float3 positionWS, float time)
{
    half drift = WaterNoise(float3(positionWS.xz / _DriftScale, time * _DriftSpeed)).r;
    half patch = WaterNoise01(float3(positionWS.xz / (_StreakScale * 4.0) + 3.7, time * _FoamChange * 0.5));
    return half2(drift, patch);
}

// ---------------------------------------------------------------------------------------------
// Ripples

float3 RippledNormal(float3 swellNormalWS, float3 positionWS, float time)
{
    float2 slope = WaterRippleSlope(positionWS.xz, _RippleScale, _RippleDrift, _RippleChange, time);

    // Far ripples are finer than a pixel and would shimmer; let them settle into the swells.
    float distanceFade = saturate(1.0 - distance(positionWS, _WorldSpaceCameraPos) / _RippleFadeDistance);
    slope *= _RippleStrength * lerp(0.1, 1.0, distanceFade);
    return normalize(swellNormalWS + float3(-slope.x, 0.0, -slope.y));
}

// ---------------------------------------------------------------------------------------------
// Scene

struct WaterScene
{
    float2 uv;            // screen uv the bed was read at
    float3 bedWS;         // the opaque surface behind the water
    float  verticalDepth; // metres of water straight above the bed
    float  viewDepth;     // metres of water along the view ray
};

// The bed is placed along this pixel's view ray at the scene's eye depth: one multiply-add
// instead of an inverse view-projection. For the bent read the ray is a pixel or two off,
// which the refraction is anyway.
WaterScene ReadSceneAt(float2 uv, float3 surfaceWS, float3 rayWS, float surfaceEye)
{
    WaterScene scene;
    scene.uv = uv;
    float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
    float along = sceneEye / surfaceEye;
    scene.bedWS = _WorldSpaceCameraPos + rayWS * along;
    scene.verticalDepth = surfaceWS.y - scene.bedWS.y;
    scene.viewDepth = (along - 1.0) * length(rayWS);
    return scene;
}

// The bed seen through the rippled surface. Bending grows with the water under the pixel, so
// the waterline itself stays put; a bent read that lands on something in front of the water
// (a reed, a rock above the surface) is thrown away for the straight one.
WaterScene ReadScene(float2 uv, float3 surfaceWS, float3 normalWS)
{
    float3 rayWS = surfaceWS - _WorldSpaceCameraPos;
    // Eye depth of the surface: its distance along the camera's forward axis.
    float surfaceEye = dot(rayWS, -UNITY_MATRIX_V[2].xyz);
    WaterScene straight = ReadSceneAt(uv, surfaceWS, rayWS, surfaceEye);
    float bend = _Refraction * saturate(straight.viewDepth);
    WaterScene scene = ReadSceneAt(uv + normalWS.xz * bend, surfaceWS, rayWS, surfaceEye);
    if (scene.viewDepth <= 0.0)
        scene = straight;
    scene.verticalDepth = max(0.0, scene.verticalDepth);
    scene.viewDepth = max(0.0, scene.viewDepth);
    return scene;
}

// ---------------------------------------------------------------------------------------------
// Body

half3 BodyColour(WaterScene scene, half drift)
{
    half3 colour = lerp(_ShoreColor.rgb, _ShallowColor.rgb, smoothstep(0.0, _ShallowDepth, scene.verticalDepth));
    colour = lerp(colour, _DeepColor.rgb, smoothstep(_ShallowDepth, _DeepDepth, scene.verticalDepth));

    // Broad, slow drift between a slightly shallower and slightly deeper tint. Kept faint: a
    // strong blotch pattern is what reads as fake.
    return lerp(colour, drift > 0.0 ? _ShallowColor.rgb : _DeepColor.rgb, abs(drift) * _DriftStrength);
}

// ---------------------------------------------------------------------------------------------
// See-through

// How much of the body colour covers the bed: none at the waterline, up to _MaxOpacity once
// the light has crossed a few _Clarity lengths of water.
half BodyOpacity(WaterScene scene)
{
    return (1.0 - exp(-scene.viewDepth / _Clarity)) * _MaxOpacity;
}

half3 SeenBed(WaterScene scene)
{
    half3 bed = SampleSceneColor(scene.uv);
    // The bed takes the water's tint as soon as it is under any water at all.
    return bed * lerp(1.0, _BedTint.rgb, saturate(scene.verticalDepth / _ShallowDepth));
}

// One hard shadow-map sample, faded out with the shadow distance like URP's own lights.
half SunShadowAt(float3 positionWS)
{
    half shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(positionWS));
    return lerp(shadow, 1.0, GetMainLightShadowFade(positionWS));
}

// ---------------------------------------------------------------------------------------------
// Caustics

half3 Caustics(WaterScene scene, float3 normalWS, float time, Light sun)
{
    half depthFade = smoothstep(0.02, 0.25, scene.verticalDepth)
                   * (1.0 - smoothstep(_CausticDepth * 0.4, _CausticDepth, scene.verticalDepth));
    // A web finer than a pixel turns to shimmer; fade it out over distance.
    half distanceFade = 1.0 - smoothstep(_CausticFadeDistance * 0.6, _CausticFadeDistance,
                                         distance(scene.bedWS, _WorldSpaceCameraPos));
    half strength = depthFade * distanceFade;

    // Two layers of the static web drift apart while noise A warps each one differently, so
    // where they cross the pattern keeps re-forming. The ripples add a wobble on top, as light
    // bent by the surface would.
    float2 cells = scene.bedWS.xz / _CausticScale + normalWS.xz * 0.35;
    float t = time * _CausticSpeed;
    float2 warp = WaterNoise(float3(cells * 0.3, t * 2.0)).gb * _CausticWarp;
    half a = WaterCausticEdge(cells + warp + float2(0.6, 0.3) * t);
    half b = WaterCausticEdge(cells * 1.37 + float2(-warp.y, warp.x) - float2(0.4, 0.7) * t + 3.1);
    half web = saturate((1.0 - smoothstep(0.0, _CausticThickness, a))
                      + (1.0 - smoothstep(0.0, _CausticThickness, b)) * 0.6);

    // Deep and far water is most of a lake; skip the bed's shadow sample there.
    half shadow = 0.0;
    UNITY_BRANCH
    if (strength > 0.0)
        shadow = SunShadowAt(scene.bedWS);
    return web * strength * shadow * sun.color * _CausticColor.rgb;
}

// ---------------------------------------------------------------------------------------------
// Shade

half3 ShadeBody(half3 body, Light sun)
{
    half lit = sun.shadowAttenuation * sun.distanceAttenuation;
    return body * lerp(_ShadowTint.rgb, 1.0, lerp(1.0 - _ShadowStrength, 1.0, lit));
}

// ---------------------------------------------------------------------------------------------
// Reflection

// Only the source in use is read: the blend is the same for every pixel of a draw, so the
// branches cost nothing and outside a reflection volume the planar texture is never touched.
half3 ReflectedColour(float2 uv, float3 normalWS, float3 viewDirWS)
{
    half3 planar = 0.0;
    half3 probe = 0.0;
    UNITY_BRANCH
    if (_PlannerReflectionBlend < 1.0)
        planar = SAMPLE_TEXTURE2D_LOD(_PlanarReflectionTexture, sampler_PlanarReflectionTexture,
                                      uv + normalWS.xz * _ReflectionDistortion, 0).rgb;
    UNITY_BRANCH
    if (_PlannerReflectionBlend > 0.0)
        probe = GlossyEnvironmentReflection(reflect(-viewDirWS, normalWS), _ProbeRoughness, 1.0);
    return lerp(planar, probe, _PlannerReflectionBlend);
}

half ReflectionAmount(float3 normalWS, float3 viewDirWS)
{
    half fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _FresnelPower);
    return lerp(_ReflectionMinimum, 1.0, fresnel) * _ReflectionStrength;
}

// ---------------------------------------------------------------------------------------------
// Foam

// A crisp threshold that stays one pixel soft at any distance.
half CrispStep(float edge, float value)
{
    float width = max(fwidth(value), 1e-4);
    return smoothstep(edge - width, edge + width, value);
}

half ShoreFoam(WaterScene scene, float3 positionWS, float time)
{
    // Against a steep rock the bed straight below can be deep while the rock face is right
    // there; the distance along the view ray catches that contact too.
    float depth = min(scene.verticalDepth, scene.viewDepth);
    // One fetch drives both: noise A breaks up the rim, noise B breaks up the lines.
    half4 noise = WaterNoise(float3(positionWS.xz / _FoamNoiseScale, time * _FoamChange));

    // At the waterline any noise clears the threshold; further out less and less does.
    half rim = CrispStep(saturate(depth / _RimWidth), saturate(noise.r * 0.75 + 0.5));

    // Bands of constant depth whose phase grows with time travel toward the shore.
    float reach = saturate(1.0 - depth / _ShoreLineReach);
    float band = abs(frac(depth / _ShoreLineSpacing + time * _ShoreLineSpeed) - 0.5) * 2.0;
    half lines = CrispStep(1.0 - _ShoreLineWidth * reach, 1.0 - band) * reach;
    lines *= CrispStep(0.45, saturate(noise.a * 0.75 + 0.5));

    return max(rim, lines);
}

half SurfaceStreaks(float3 positionWS, float time, half patch)
{
    float3 cells = float3(positionWS.xz / _StreakScale + float2(0.6, 0.4) * time * _StreakDrift, time * _FoamChange);
    // Thin, wandering lines where the noise crosses zero. Value over slope is the distance to
    // that crossing, so the lines keep one width; thresholding the value alone made them
    // lumpy, fat where the noise is flat.
    half4 noise = WaterNoise(cells);
    float toCrossing = abs(noise.r) / max(length(noise.gb), 0.1);
    half streak = 1.0 - CrispStep(_StreakWidth, toCrossing);
    // Only in drifting patches, so the open water is mostly clear.
    return streak * CrispStep(1.0 - _StreakCoverage, patch);
}

// ---------------------------------------------------------------------------------------------
// Glints

half3 SunGlints(float3 normalWS, float3 viewDirWS, Light sun)
{
    float3 halfDir = normalize(sun.direction + viewDirWS);
    half highlight = pow(saturate(dot(normalWS, halfDir)), _GlintPower);
    half glint = smoothstep(_GlintThreshold, _GlintThreshold + _GlintSoftness, highlight);
    return glint * sun.shadowAttenuation * sun.color * _GlintColor.rgb;
}

#endif
