#ifndef SPACEGAME_CLOTHWIND_INCLUDED
#define SPACEGAME_CLOTHWIND_INCLUDED

// Shared by every pass of ClothWind.shader. The forward, shadow and depth passes must all run
// exactly the same displacement, or a billowing cape casts a rigid shadow and z-fights itself.
//
// This is the garment counterpart to SailCloth.hlsl. A sail is a flat plane with known UVs and
// three lashed edges; a cape is a skinned mesh hanging off a character, so the two differ in
// where the pinning comes from:
//
//   Sail  - pinned by UV, because dune_foil_rig.py authors U/V as real distances along the spar.
//   Cape  - pinned by height, because the collar is stitched to the shoulders and the hem is
//           free. _AnchorAxis / _FreeLength describe that span in object space.
//
// The displacement runs on the vertex AFTER skinning, so it layers on top of the Animator
// rather than fighting it.

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4  _BaseColor;
    half   _Smoothness;
    half   _Metallic;
    half   _Backlight;

    float  _WeaveScale;
    half   _WeaveDepth;

    float4 _WindDirection;
    half   _WindStrength;
    half   _Turbulence;

    float  _WaveSpeed;
    float  _WaveLength;
    float  _FlutterAmp;
    float  _FlutterFreq;
    float  _FlutterSpeed;

    half   _GustSpeed;
    half   _GustAmount;

    float  _AnchorAxis;
    float  _AnchorOrigin;
    float  _FreeLength;
    half   _Stiffness;
    float  _MaxStretch;
CBUFFER_END

// Cheap value noise, same as the sail uses. Good enough for cloth and far cheaper than
// anything gradient-based at two octaves per vertex.
float ClothHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
}

float ClothNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);           // smoothstep the interpolant

    float a = ClothHash(i);
    float b = ClothHash(i + float2(1, 0));
    float c = ClothHash(i + float2(0, 1));
    float d = ClothHash(i + float2(1, 1));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y) * 2.0 - 1.0;
}

// A faint woven texture, so a large unlit panel of cloth is not one dead colour.
half ClothWeave(float2 uv)
{
    float2 w = uv * _WeaveScale;
    float threads = (sin(w.x * 6.2831) * sin(w.y * 6.2831)) * 0.5 + 0.5;
    return 1.0 - _WeaveDepth * (1.0 - threads);
}

// How free each point of the garment is to move.
//
// 0 where the cloth is stitched to the body, 1 at the loose hem. Measured along one object-space
// axis: the collar sits at _AnchorOrigin and the hem _FreeLength away from it. _FreeLength is
// signed so the hem can lie on either side of the anchor without inverting the model.
//
// This is the load-bearing part. Without it the wind translates every vertex equally and the
// whole cape slides off the character as a rigid sheet.
float ClothFreedom(float3 positionOS)
{
    int axis = (int)round(_AnchorAxis);
    float along = (axis == 0) ? positionOS.x : ((axis == 1) ? positionOS.y : positionOS.z);

    float len = (abs(_FreeLength) < 1e-4) ? 1e-4 : _FreeLength;
    float t = saturate((along - _AnchorOrigin) / len);

    // _Stiffness bends the curve: higher keeps the cloth rigid near the collar and throws the
    // motion out to the hem, which is how a heavy cape actually hangs.
    return pow(t, _Stiffness);
}

// Displace and re-normal one cloth vertex. positionOS and normalOS are modified in place.
void ShapeCloth(float3 basePositionOS, inout float3 positionOS, inout float3 normalOS)
{
    float freedom = ClothFreedom(basePositionOS);
    if (freedom <= 0.0)
        return;

    // Wind arrives in world space; bring it into object space so the cape blows the same way
    // regardless of which direction the character happens to be facing.
    float3 windOS = mul((float3x3)GetWorldToObjectMatrix(), _WindDirection.xyz);
    float windLen = length(windOS);
    windOS = windLen > 1e-5 ? windOS / windLen : float3(0, 0, 1);

    // Object space is not guaranteed to be metres. Blender FBXs land in this project at a
    // lossyScale of 100, so a displacement of "0.3" in object units would be 30 m in the world.
    // Convert the metre-valued amplitudes below into object units so the cloth moves by the
    // distance the material actually asks for.
    float objectToWorld = length(mul((float3x3)GetObjectToWorldMatrix(), windOS));
    float toObjectUnits = objectToWorld > 1e-5 ? 1.0 / objectToWorld : 1.0;

    // Phase travels along the wind, so the billow rolls down the cloth instead of every vertex
    // pulsing in lockstep.
    float3 posWS = TransformObjectToWorld(positionOS);
    float phase = dot(posWS, _WindDirection.xyz) / max(_WaveLength, 0.05);
    float t = _Time.y;

    // --- gust ------------------------------------------------------------
    // Slow swell and drop of the whole wind, so the cape breathes instead of running at one
    // fixed amplitude forever.
    float gust = 1.0 - _GustAmount * (0.5 - 0.5 * sin(t * _GustSpeed));

    // --- billow ----------------------------------------------------------
    float billow = sin(phase - t * _WaveSpeed);

    // --- flutter ---------------------------------------------------------
    // Two octaves of travelling noise, strongest at the hem, which is the edge that shakes.
    float wave = ClothNoise(float2(phase * _FlutterFreq - t * _FlutterSpeed,
                                   posWS.y * _FlutterFreq + t * _FlutterSpeed * 0.5))
               + 0.5 * ClothNoise(float2(phase * _FlutterFreq * 2.7 + t * _FlutterSpeed * 1.7,
                                         posWS.y * _FlutterFreq * 3.0 - t * _FlutterSpeed * 0.9));
    float flutter = wave * _FlutterAmp * freedom;

    // Sideways sway, so the cloth snakes rather than flapping on a single plane.
    float3 sideOS = normalize(cross(windOS, float3(0, 1, 0)) + float3(1e-5, 0, 0));
    float sway = sin(phase * 0.6 - t * _WaveSpeed * 0.8) * _Turbulence;

    float push = (0.65 + 0.35 * billow) * _WindStrength + flutter;
    float3 offsetOS = (windOS * push + sideOS * sway * _WindStrength) * freedom * gust * toObjectUnits;

    // Never let the cloth tear away from the body it is stitched to.
    float maxLen = _MaxStretch * freedom * toObjectUnits;
    float lenSq = dot(offsetOS, offsetOS);
    if (lenSq > maxLen * maxLen && lenSq > 1e-8)
        offsetOS *= maxLen * rsqrt(lenSq);

    positionOS += offsetOS;

    // Re-normal from the slope of the displacement. Without this the cape billows but still
    // lights as though it were flat, which reads as a printed pattern rather than a shape.
    float3 bend = windOS * billow + sideOS * sway;
    normalOS = normalize(normalOS - bend * freedom * _WindStrength * 1.2);
}

#endif // SPACEGAME_CLOTHWIND_INCLUDED
