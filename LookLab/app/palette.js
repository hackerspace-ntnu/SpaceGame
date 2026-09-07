// Port of PaletteShape.Default and PastelPalette.Build in
// Assets/Game/Scripts/World/Environment/ColorGrade/. Three ports of this lattice now
// exist — C# (ships), Python (checks), JS (this) — so `python3 tools/palette_preview.py
// --check` compares this one against the Python one entry for entry. Change one, the
// check fails until all three agree.

export const DEFAULT_SHAPE = Object.freeze({
  hueCount: 16,
  lightnesses: [0.92, 0.82, 0.72, 0.61, 0.49, 0.36],
  chromaFractions: [0.5, 1.0],
  chromaCeiling: 0.20,
  neutralCount: 12,
  neutralMinL: 0.16,
  neutralMaxL: 0.97,
});

const GAMUT_FIT_ITERATIONS = 16;
const GAMUT_EPSILON = 1e-4;

/** Oklch to linear RGB. Unclamped, so inGamut can see the overflow. */
function oklchToLinear(lightness, chroma, hueRadians) {
  const a = chroma * Math.cos(hueRadians);
  const b = chroma * Math.sin(hueRadians);
  const l = (lightness + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const m = (lightness - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const s = (lightness - 0.0894841775 * a - 1.2914855480 * b) ** 3;
  return [
    +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s,
  ];
}

function inGamut(lightness, chroma, hueRadians) {
  return oklchToLinear(lightness, chroma, hueRadians)
    .every((c) => c >= -GAMUT_EPSILON && c <= 1 + GAMUT_EPSILON);
}

/**
 * The largest chroma up to `chroma` that still lands inside sRGB. Clamping an
 * out-of-gamut colour instead shifts its hue and drops its lightness, so an authored
 * entry quietly comes out darker and dirtier than the ramp says.
 */
function fitChroma(lightness, chroma, hueRadians) {
  if (inGamut(lightness, chroma, hueRadians)) return chroma;
  let low = 0;
  let high = chroma;
  for (let i = 0; i < GAMUT_FIT_ITERATIONS; i++) {
    const mid = 0.5 * (low + high);
    if (inGamut(lightness, mid, hueRadians)) low = mid;
    else high = mid;
  }
  return low;
}

/**
 * Unity's Color.gamma (LinearToGammaSpace). Note the 0.41666 exponent — Unity's own
 * approximation, not 1/2.4. The palette entries in C# go through this, so this port has
 * to as well or the two disagree in the third decimal.
 */
export function linearToGamma(c) {
  if (c <= 0) return 0;
  if (c <= 0.0031308) return c * 12.92;
  if (c <= 1) return 1.055 * Math.pow(c, 0.41666) - 0.055;
  return Math.pow(c, 0.45454545);
}

/** Unity's Color.linear (GammaToLinearSpace). */
export function gammaToLinear(c) {
  if (c <= 0.04045) return c / 12.92;
  if (c < 1) return Math.pow((c + 0.055) / 1.055, 2.4);
  return Math.pow(c, 2.2);
}

/** Must stay in lockstep with PastelPalette.LinearToOklab and the HLSL shader. */
export function linearToOklab([r, g, b]) {
  let l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b;
  let m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b;
  let s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b;
  l = Math.cbrt(Math.max(l, 0));
  m = Math.cbrt(Math.max(m, 0));
  s = Math.cbrt(Math.max(s, 0));
  return [
    0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
    1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
    0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s,
  ];
}

function oklchToSrgb(lightness, chroma, hueRadians) {
  const fitted = fitChroma(lightness, chroma, hueRadians);
  return oklchToLinear(lightness, fitted, hueRadians)
    .map((c) => linearToGamma(Math.min(1, Math.max(0, c))));
}

/**
 * The palette in the order PastelPalette.Build emits it, as gamma-space sRGB in 0..1 —
 * the same values PastelPalette hands back as a Color[].
 */
export function buildPalette(shape) {
  const colors = [];

  for (let h = 0; h < shape.hueCount; h++) {
    const hue = (h * 2 * Math.PI) / shape.hueCount;
    for (const lightness of shape.lightnesses) {
      // fitChroma of the ceiling IS the in-gamut maximum.
      const ceiling = fitChroma(lightness, shape.chromaCeiling, hue);
      for (const fraction of shape.chromaFractions) {
        colors.push(oklchToSrgb(lightness, ceiling * fraction, hue));
      }
    }
  }

  for (let n = 0; n < shape.neutralCount; n++) {
    const t = n / (shape.neutralCount - 1);
    colors.push(
      oklchToSrgb(shape.neutralMinL + (shape.neutralMaxL - shape.neutralMinL) * t, 0, 0));
  }

  return colors;
}

export function hexOf([r, g, b]) {
  const byte = (c) => Math.round(c * 255).toString(16).toUpperCase().padStart(2, '0');
  return '#' + byte(r) + byte(g) + byte(b);
}

/**
 * The palette as a `width` x 2 RGBA32F payload: row 0 is the linear RGB written out,
 * row 1 the Oklab matched against — exactly the two arrays the HLSL shader uploads.
 *
 * The gamma round trip is deliberate and must not be "simplified" away. C# builds each
 * entry as a gamma-space Color and the render feature reads back `.linear`, so the value
 * the shader writes has been through Unity's two approximations. Skipping the trip here
 * would make the lab a fraction brighter than the game for no visible reason.
 */
export function paletteTexture(colors, width) {
  const data = new Float32Array(width * 2 * 4);
  colors.forEach((gamma, i) => {
    const linear = gamma.map(gammaToLinear);
    const oklab = linearToOklab(linear);
    data.set([linear[0], linear[1], linear[2], 1], i * 4);
    data.set([oklab[0], oklab[1], oklab[2], 1], (width + i) * 4);
  });
  return data;
}
