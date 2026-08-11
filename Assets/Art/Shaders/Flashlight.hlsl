#ifndef SPACEGAME_FLASHLIGHT_INCLUDED
#define SPACEGAME_FLASHLIGHT_INCLUDED

// Custom flashlight contribution with a flatter long-distance falloff than URP's
// standard additional-light path. Pushed as shader globals from Flashlight.cs so it's
// effectively free — no per-light loop iteration, no shadow sampling.
//
// Globals (set by Flashlight.cs):
//   _FlashlightPos.xyz       world position
//   _FlashlightDir.xyz       world forward (unit length)
//   _FlashlightColor.rgb     color premultiplied by longThrowIntensity
//   _FlashlightParams        x = cos(outerAngle/2), y = cos(innerAngle/2),
//                            z = range, w = enabled (0 or 1)
//   _FlashlightFalloff       x = k (1 / (1 + k*d)), y = rangeFadeStart (0..1)

float4 _FlashlightPos;
float4 _FlashlightDir;
float4 _FlashlightColor;
float4 _FlashlightParams;
float4 _FlashlightFalloff;

// Returns the additive light contribution for the flashlight at world position posWS
// with surface normal N. wrap is the same wrapped-diffuse term used elsewhere.
float3 SampleFlashlight(float3 posWS, float3 N, float wrap)
{
    if (_FlashlightParams.w < 0.5)
        return 0.0;

    float3 toFrag = posWS - _FlashlightPos.xyz;
    float  d      = length(toFrag);
    if (d > _FlashlightParams.z)
        return 0.0;

    float3 L = toFrag / max(d, 0.0001);

    // Cone mask with a continuous center-to-edge falloff (real flashlights are
    // brightest on axis, not a uniform bright disc). cosA = 1 on axis, cosOut at
    // the outer ring. Normalize to 0..1 across the cone, then raise to a power so
    // the bright hotspot is small and the rest is soft glow.
    float cosA   = dot(L, _FlashlightDir.xyz);
    float cosOut = _FlashlightParams.x;
    if (cosA <= cosOut)
        return 0.0;
    float cone = saturate((cosA - cosOut) / max(1.0 - cosOut, 0.0001));
    // pow > 1 = small bright center fading into soft halo. Tweak for hotspot tightness.
    cone = pow(cone, 1.8);

    // Flatter than inverse-square: keeps distant surfaces lit.
    float k         = _FlashlightFalloff.x;
    float distAtten = 1.0 / (1.0 + k * d);

    // Soft cutoff near the max range to avoid a hard edge.
    float fadeStart = _FlashlightFalloff.y;
    float rangeT    = saturate((d / _FlashlightParams.z - fadeStart) / max(1.0 - fadeStart, 0.0001));
    distAtten *= 1.0 - rangeT;

    // Wrapped diffuse so off-angle surfaces still read.
    float ndl = saturate((dot(N, -L) + wrap) / (1.0 + wrap));

    return _FlashlightColor.rgb * (cone * distAtten * ndl);
}

#endif // SPACEGAME_FLASHLIGHT_INCLUDED
