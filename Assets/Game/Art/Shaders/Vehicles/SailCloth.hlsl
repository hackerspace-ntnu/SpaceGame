#ifndef SPACEGAME_SAILCLOTH_INCLUDED
#define SPACEGAME_SAILCLOTH_INCLUDED

// Shared by every pass of SailCloth.shader. The forward, shadow and depth passes must all run
// exactly the same displacement, or a billowing sail casts a flat shadow and z-fights itself.

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4  _BaseColor;
    half   _Smoothness;

    float  _WeaveScale;
    half   _WeaveDepth;

    half   _Billow;
    float  _BillowDepth;
    half   _DraftPosition;

    half   _Luff;
    float  _FlutterAmp;
    float  _FlutterFreq;
    float  _FlutterSpeed;

    half   _Hoist;
    float4 _WindDirection;

    half   _Backlight;
CBUFFER_END

// Cheap value noise. Good enough for cloth, and far cheaper than anything gradient-based at
// two octaves per vertex.
float SailHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
}

float SailNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);           // smoothstep the interpolant

    float a = SailHash(i);
    float b = SailHash(i + float2(1, 0));
    float c = SailHash(i + float2(0, 1));
    float d = SailHash(i + float2(1, 1));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y) * 2.0 - 1.0;
}

// A faint canvas weave, so a large flat sail is not a single dead colour.
half WeavePattern(float2 uv)
{
    float2 w = uv * _WeaveScale;
    float threads = (sin(w.x * 6.2831) * sin(w.y * 6.2831)) * 0.5 + 0.5;
    return 1.0 - _WeaveDepth * (1.0 - threads);
}

// How free each point of the sail is to move.
//
// The luff (u = 0) is lashed to the post, and the foot and head are lashed to their spars, so
// all three edges are pinned hard. Everything in between is cloth.
float SailFreedom(float2 uv)
{
    float alongChord = smoothstep(0.0, 0.22, uv.x);     // released aft of the luff
    float alongSpar  = smoothstep(0.0, 0.10, uv.y) * smoothstep(0.0, 0.10, 1.0 - uv.y);
    return alongChord * alongSpar;
}

// Depth of the belly across the chord.
//
// Peaks at _DraftPosition (about 40% aft of the luff on a real sail) and returns to zero at
// both the luff and the leech, since both edges are held. Two half-cosines rather than one
// symmetric curve, so the draft can sit forward of centre where it belongs.
float DraftProfile(float u)
{
    float d = saturate(_DraftPosition);
    float t = (u < d) ? (u / max(d, 1e-4)) : ((1.0 - u) / max(1.0 - d, 1e-4));
    return sin(saturate(t) * 1.5707963);                // 0 at both edges, 1 at the draft
}

// Displace and re-normal one sail vertex. positionOS and normalOS are modified in place.
void ShapeSail(float2 uv, inout float3 positionOS, inout float3 normalOS)
{
    float freedom = SailFreedom(uv);
    if (freedom <= 0.0)
        return;

    // Which way is leeward, in object space. The sail bellies away from the wind, so a sail
    // that swings across the deck flips its belly with it rather than staying inflated the
    // wrong way.
    float3 windOS = mul((float3x3)GetWorldToObjectMatrix(), _WindDirection.xyz);
    float windLen = length(windOS);
    windOS = windLen > 1e-5 ? windOS / windLen : float3(0, 0, 1);

    // Push along whichever face of the sail the wind is actually on.
    float side = dot(normalOS, windOS) < 0.0 ? -1.0 : 1.0;
    float3 pushDir = normalize(normalOS) * side;

    // Object space is not guaranteed to be metres. A mesh authored by scaling a primitive
    // carries that scale on its transform, so a displacement of "1.35" in object space can be
    // a hundred metres in the world. Convert the metre-valued amplitudes below into object
    // units so the sail bellies by the distance the material actually asks for.
    float objectToWorld = length(mul((float3x3)GetObjectToWorldMatrix(), normalize(pushDir)));
    float toObjectUnits = objectToWorld > 1e-5 ? 1.0 / objectToWorld : 1.0;

    // --- billow ---------------------------------------------------------
    // Tapered toward the head: a sail carries less draft aloft than at the foot.
    float headTaper = lerp(1.0, 0.55, uv.y);
    float belly = DraftProfile(uv.x) * headTaper * _Billow * _BillowDepth * _Hoist;

    // --- flutter --------------------------------------------------------
    // Weighted to the leech, which is the edge that shakes, and travelling across the sail so
    // it ripples rather than pulsing in place.
    float t = _Time.y * _FlutterSpeed;
    float leech = uv.x * uv.x;
    float wave = SailNoise(float2(uv.x * _FlutterFreq * 2.0 - t, uv.y * _FlutterFreq + t * 0.5))
               + 0.5 * SailNoise(float2(uv.x * _FlutterFreq * 5.0 + t * 1.7,
                                        uv.y * _FlutterFreq * 3.0 - t * 0.9));
    float flutter = wave * leech * _Luff * _FlutterAmp * _Hoist;

    float offset = (belly + flutter) * freedom * toObjectUnits;
    positionOS += pushDir * offset;

    // Re-normal from the analytic slope of the draft curve. Without this the sail inflates but
    // still lights as though it were flat, which reads as a printed curve rather than a shape.
    float d = saturate(_DraftPosition);
    float slope = (uv.x < d) ? 1.0 : -1.0;
    float curvature = slope * _Billow * _BillowDepth * headTaper * freedom * 1.2;

    // Bend the normal across the chord. The chord direction in object space is the sail's
    // in-plane axis perpendicular to the spar; approximate it from the normal and world up,
    // which is accurate enough at the angles a sail actually sits at.
    float3 sparOS = normalize(mul((float3x3)GetWorldToObjectMatrix(), float3(0, 1, 0)));
    float3 chordOS = normalize(cross(sparOS, normalize(normalOS)));
    normalOS = normalize(normalOS - chordOS * curvature * side);
}

#endif // SPACEGAME_SAILCLOTH_INCLUDED
