#ifndef SPACEGAME_STYLIZED_EYE_INCLUDED
#define SPACEGAME_STYLIZED_EYE_INCLUDED

// Shared by every pass of StylizedEye.shader. The layout must be identical in all of them, or the
// material stops being SRP-batcher compatible.

TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);
TEXTURE2D(_EmissionMap);    SAMPLER(sampler_EmissionMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4  _BaseColor;
    half   _Smoothness;
    half4  _EmissionColor;

    half4  _LidColor;
    half   _LidSmoothness;
    float4 _LidEdges;
CBUFFER_END

// An edge at or past this many radians is a lid tucked all the way behind the eyeball. It is the
// "no lid at all" case and is switched off outright rather than drawn: at +-PI the edge lies on the
// atan2 wrap, and a soft step there paints a hairline of skin down the back of the eye.
#define LID_RETRACTED 3.14

// Cap on the anti-aliasing width, in radians. At the eye's two side poles every lid edge meets, the
// angle stops meaning anything and its screen-space rate runs off to infinity; uncapped, that
// smears a blot of half-lid across the pole.
#define LID_MAX_SOFTNESS 0.15

// How far a point on the eyeball has turned about the eye's horizontal axis, in radians: 0 at the
// pupil, +PI/2 straight up, +-PI straight behind.
//
// Read off the UV rather than the mesh, because the UV IS the eye's frame: StylizedEyeBuilder
// unwraps every eye equirectangularly about its gaze, with v rising toward the character's up.
// This is its Direction(u, v) run on the GPU, keeping only the two components the angle needs.
// A lid defined this way is hinged on the eyeball, so it follows the pupil wherever the eye is
// turned -- which is what a lid on eyes that bulge out of the side of a head should do.
float EyelidAngle(float2 uv, out float softness)
{
    float longitude = (uv.x - 0.5) * TWO_PI;
    float latitude  = (uv.y - 0.5) * PI;

    float2 arm = float2(cos(latitude) * cos(longitude), sin(latitude));   // (gaze, up)

    // Measured on the continuous (gaze, up) pair rather than on the angle, so the atan2 wrap behind
    // the eye does not register as an edge.
    softness = min(length(fwidth(arm)) / max(length(arm), 1e-4), LID_MAX_SOFTNESS);
    return atan2(arm.y, arm.x);
}

// How much of this point the lids cover (x) and how much of it is the dark line along their edges
// (y), both 0..1. _LidEdges is (upper edge, lower edge, lash width, lash darkening): the upper lid
// covers every angle above its edge, the lower lid every angle below its own.
half2 EyelidCoverage(float2 uv)
{
    float softness;
    float angle = EyelidAngle(uv, softness);

    float upperEdge = _LidEdges.x;
    float lowerEdge = _LidEdges.y;
    float lashWidth = _LidEdges.z;

    half upper = 0;
    half upperLash = 0;
    if (upperEdge < LID_RETRACTED)
    {
        upper = smoothstep(upperEdge - softness, upperEdge + softness, angle);
        upperLash = upper * (1 - smoothstep(upperEdge + lashWidth - softness,
                                            upperEdge + lashWidth + softness, angle));
    }

    half lower = 0;
    half lowerLash = 0;
    if (lowerEdge > -LID_RETRACTED)
    {
        lower = smoothstep(-lowerEdge - softness, -lowerEdge + softness, -angle);
        lowerLash = lower * (1 - smoothstep(-lowerEdge + lashWidth - softness,
                                            -lowerEdge + lashWidth + softness, -angle));
    }

    return half2(saturate(upper + lower), saturate(upperLash + lowerLash));
}

#endif
