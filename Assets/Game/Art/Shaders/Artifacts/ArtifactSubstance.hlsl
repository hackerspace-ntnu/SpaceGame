#ifndef SPACEGAME_ARTIFACTSUBSTANCE_INCLUDED
#define SPACEGAME_ARTIFACTSUBSTANCE_INCLUDED

// Shared by the four substances the new artifacts leave in the world: foam (FoamSurface),
// the slick film (SlickSheen), ice (FrozenStatue) and the storm cloud (StormCloud).
//
// This file is the PALETTE CONTRACT as much as it is a noise library. Everything drawn in
// this game is snapped, per pixel, to the nearest of 204 committed colours by
// PastelQuantize.shader (Environment.md), so an effect does not exist because it was drawn —
// it exists because it landed on a DIFFERENT palette entry from what is behind it. Every
// number below was measured against tools/palette_golden.txt rather than guessed, and the
// four shaders' defaults are set from these budgets:
//
//   LIGHTNESS. The palette's six lightness rows plus the 12-step grey ramp put entry
//   boundaries roughly every 0.065-0.08 Oklab L. A shading gradient finer than that renders
//   as one flat colour however carefully it was authored. So all four shaders BAND their
//   shading into a handful of explicit steps rather than ramping: the banding is authored,
//   which means it is controllable, instead of emerging from the quantizer, which means it
//   is not. This is the same choice JetFlame.shader made, for the same reason
//   (GDC-L1-TECH-0004: a coherent stylized read beats fidelity the frame cannot hold).
//
//   CHROMA. There are only two chroma rings, and below about C 0.02 a colour falls off them
//   onto the GREY ramp entirely. That is the trap under any effect described as a "faint"
//   tint: at C 0.015 a full 360-degree hue sweep collapsed to four near-neutral entries and
//   read as dirty grey; the same sweep at C 0.07 hit 11 distinct entries out of 12 bands.
//   A tint here is made faint by COVERAGE — how little of the screen it occupies — never by
//   desaturating it, because desaturating it deletes it.
//
//   HUE. 16 hue columns, 22.5 degrees apart; the first hue step that changes entry at
//   L 0.80 / C 0.10 is 13 degrees. A hue drift smaller than that is not visible at all.
//
//   STAY IN ONE COLUMN. A near-neutral ladder walks off its hue column onto the grey ramp as
//   it darkens, so its bands alternate between tinted and neutral and the substance reads
//   dirty. Hold hue and chroma FIXED down a ladder and vary only lightness, and the bands
//   walk down one column of the lattice.
//
//   THE INK IS FREE CONTRAST. The quantizer's ink pass darkens along lightness gradients
//   (threshold 0.03 L) and along depth gradients. So a HARD-EDGED effect gets outlined by
//   the post pass for nothing, and a softly feathered one does not. Where an effect has to
//   be found rather than admired, its boundary is a step, not a fade.
//
// None of the four shaders that include this file reads a UV or a vertex colour. They are
// shaded from object space, world space and the surface normal only, so nothing here can
// break on a mesh whose UV convention turns out to differ from what was assumed.

// ---------------------------------------------------------------------------------------
// Value noise
// ---------------------------------------------------------------------------------------
// Procedural rather than a texture, the same trade RepulsorAirWarp.shader and JetFlame.shader
// already made: these materials ship with no map assigned, and a sampler here would be a
// fetch and a memory dependency for something a few instructions do well enough.

float SubstanceHash(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float SubstanceNoise(float3 p)
{
    float3 cell = floor(p);
    float3 f = p - cell;
    f = f * f * (3.0 - 2.0 * f);           // smoothstep the interpolant; bilinear alone creases

    float n000 = SubstanceHash(cell + float3(0, 0, 0));
    float n100 = SubstanceHash(cell + float3(1, 0, 0));
    float n010 = SubstanceHash(cell + float3(0, 1, 0));
    float n110 = SubstanceHash(cell + float3(1, 1, 0));
    float n001 = SubstanceHash(cell + float3(0, 0, 1));
    float n101 = SubstanceHash(cell + float3(1, 0, 1));
    float n011 = SubstanceHash(cell + float3(0, 1, 1));
    float n111 = SubstanceHash(cell + float3(1, 1, 1));

    return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
}

// Two octaves, no more. A third costs eight more hashes and, once the result has been banded
// into four flats, changes which flat perhaps one pixel in a hundred lands on.
float SubstanceTurbulence(float3 p)
{
    return SubstanceNoise(p) * 0.65 + SubstanceNoise(p * 2.17 + 11.3) * 0.35;
}

// ---------------------------------------------------------------------------------------
// Banding
// ---------------------------------------------------------------------------------------

// Quantize a 0..1 shading term to `count` flats, INCLUSIVE of both ends: with count = 4 the
// results are exactly 0, 1/3, 2/3, 1, so a four-colour ladder is indexed edge to edge and the
// brightest band is actually reached. The naive floor(v * count) / count never returns 1 and
// quietly loses the top band, which shows up as a substance that never catches a highlight.
float SubstanceBand(float v, float count)
{
    float steps = max(count, 2.0);
    return floor(saturate(v) * (steps - 1.0) + 0.5) / (steps - 1.0);
}

// Pick one of four ladder colours, `a` darkest. Ladders are authored as four separate colour
// properties rather than as two ends and a lerp, because a lerp is even in LINEAR RGB while
// the palette's rows are even in Oklab L: interpolating between a shadow and a highlight
// bunches three of the four bands into the top row of the lattice, where they snap together
// into one flat. Four authored colours land on four entries chosen off the golden file.
float3 SubstanceLadder4(float v, float3 a, float3 b, float3 c, float3 d)
{
    // Rounded rather than floored, so the two end bands are half width and the ladder is
    // indexed edge to edge: floor never returns the top index and the highlight is lost.
    float index = floor(saturate(v) * 3.0 + 0.5);
    return index < 1.5 ? (index < 0.5 ? a : b)
                       : (index < 2.5 ? c : d);
}

// Three-band form, for a substance whose palette column has only three usable rows before it
// runs into the grey ramp — which is the storm cloud's situation at the dark end.
float3 SubstanceLadder3(float v, float3 a, float3 b, float3 c)
{
    float index = floor(saturate(v) * 2.0 + 0.5);
    return index < 0.5 ? a : (index < 1.5 ? b : c);
}

// ---------------------------------------------------------------------------------------
// Palette bridge
// ---------------------------------------------------------------------------------------

// Oklch to linear RGB. Must stay in lockstep with PastelPalette.OklchToLinear and with
// OklabToLinear in PastelQuantize.shader — this is how a shader aims a colour AT a palette
// entry instead of hoping one is nearby. Used to generate the slick film's iridescence
// analytically at a known chroma, which is the only way to guarantee the hue sweep lands on
// the lattice's vivid ring rather than dissolving into the grey ramp.
//
// No gamut fit here, unlike PastelPalette.FitChroma: a caller asking for more chroma than
// sRGB holds at that hue and lightness gets a clamped colour, which shifts its hue. Keep
// requested chroma at or below about 0.09 at mid lightness, and lower near white, which is
// what the callers' property ranges enforce.
float3 SubstanceOklchToLinear(float lightness, float chroma, float hueRadians)
{
    float3 okl = float3(lightness,
                        chroma * cos(hueRadians),
                        chroma * sin(hueRadians));

    float3 lms = float3(
        okl.x + 0.3963377774 * okl.y + 0.2158037573 * okl.z,
        okl.x - 0.1055613458 * okl.y - 0.0638541728 * okl.z,
        okl.x - 0.0894841775 * okl.y - 1.2914855480 * okl.z);
    lms = lms * lms * lms;

    return float3(
        dot(lms, float3( 4.0767416621, -3.3077115913,  0.2309699292)),
        dot(lms, float3(-1.2684380046,  2.6097574011, -0.3413193965)),
        dot(lms, float3(-0.0041960863, -0.7034186147,  1.7076147010)));
}

// ---------------------------------------------------------------------------------------
// Shading terms
// ---------------------------------------------------------------------------------------

// Wrapped diffuse, the same term AlgaeRock.shader and Flashlight.hlsl use. Both translucent
// substances here want it for the same reason: light that has scattered THROUGH a material
// keeps arriving past the terminator, so a hard N.L makes foam and ice read as painted
// plaster. `wrap` at 1 lights the full sphere and is the value a thin substance wants.
float SubstanceWrapDiffuse(float3 normalWS, float3 lightDirWS, float wrap)
{
    return saturate((dot(normalWS, lightDirWS) + wrap) / (1.0 + wrap));
}

// Grazing-angle term. abs() on the dot because these materials are drawn with Cull Off or on
// meshes whose winding is not guaranteed: a back face arrives with its normal pointing away
// and would otherwise fresnel to a constant, which is the "visible from one half of the
// compass" failure in its quietest form.
float SubstanceFresnel(float3 normalWS, float3 viewDirWS, float power)
{
    return pow(1.0 - saturate(abs(dot(normalWS, viewDirWS))), power);
}

// Triplanar-projected turbulence, so a substance has surface detail without a UV set. The
// weights are the squared normal, sharpened, which is the cheapest blend that does not smear
// the three projections into mud on a 45-degree face.
float SubstanceTriplanarTurbulence(float3 position, float3 normal, float scale, float sharpness)
{
    float3 weights = pow(abs(normal), max(sharpness, 1.0));
    weights /= max(weights.x + weights.y + weights.z, 1e-4);

    float3 p = position * scale;
    return SubstanceTurbulence(float3(p.y, p.z, 0.0)) * weights.x
         + SubstanceTurbulence(float3(p.z, p.x, 7.7)) * weights.y
         + SubstanceTurbulence(float3(p.x, p.y, 3.3)) * weights.z;
}

#endif // SPACEGAME_ARTIFACTSUBSTANCE_INCLUDED
