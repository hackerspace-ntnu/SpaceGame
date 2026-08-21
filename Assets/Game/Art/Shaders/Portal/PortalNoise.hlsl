#ifndef SPACEGAME_PORTAL_NOISE_INCLUDED
#define SPACEGAME_PORTAL_NOISE_INCLUDED

// Shared noise for the portal family — the aperture rim, the halo, the fluid in
// the gun's reservoirs and the projectile in flight.
//
// It lives in one file because those four surfaces have to look like the same
// substance. Four separate copies of "some fbm" drift apart the moment one of
// them is tweaked, and the reservoir is meant to read as the stuff the aperture
// is made of, seen through a window.
//
// Value noise rather than gradient noise: it is cheaper, it is being domain-
// warped anyway (which hides the axis-aligned artefacts value noise is blamed
// for), and every consumer here wants a soft cloud rather than a crisp field.

float PortalHash(float2 p)
{
    p = frac(p * float2(233.34, 851.73));
    p += dot(p, p + 23.45);
    return frac(p.x * p.y);
}

float PortalValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);

    // Quintic rather than cubic: the second derivative is continuous, which
    // matters because the fluid shader takes a gradient of this to fake
    // lighting and a cubic falloff makes that gradient visibly facetted.
    float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

    float a = PortalHash(i + float2(0.0, 0.0));
    float b = PortalHash(i + float2(1.0, 0.0));
    float c = PortalHash(i + float2(0.0, 1.0));
    float d = PortalHash(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float PortalFbm(float2 p, int octaves)
{
    float sum = 0.0;
    float amp = 0.5;
    float norm = 0.0;

    // Fixed bound with a break: an unbounded loop on a uniform will not unroll,
    // and every call site passes a literal anyway.
    [unroll]
    for (int i = 0; i < 6; i++)
    {
        if (i >= octaves) break;
        sum  += PortalValueNoise(p) * amp;
        norm += amp;
        p    = p * 2.03 + float2(17.3, 9.1);   // irrational-ish, to avoid tiling
        amp *= 0.5;
    }

    return sum / max(norm, 1e-5);
}

// Domain-warped fbm. This is what makes the fluid churn rather than merely
// scroll: the field is sampled at a position that is itself displaced by the
// field, so features stretch and fold instead of translating rigidly.
float PortalWarpedFbm(float2 p, float time, float strength)
{
    float2 q = float2(PortalFbm(p + float2(0.0, time * 0.15), 3),
                      PortalFbm(p + float2(5.2, 1.3 - time * 0.11), 3));

    float2 r = float2(PortalFbm(p + strength * q + float2(1.7, 9.2), 3),
                      PortalFbm(p + strength * q + float2(8.3, 2.8), 3));

    return PortalFbm(p + strength * r, 4);
}

#endif // SPACEGAME_PORTAL_NOISE_INCLUDED
