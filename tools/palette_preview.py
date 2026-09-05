#!/usr/bin/env python3
"""Renders palette-preview.png, the contact sheet for the pastel quantize filter.

A port of PastelPalette.Default() in
Assets/Game/Scripts/World/Environment/ColorGrade/PastelPalette.cs. Keeping it in
Python means the palette can be checked — entry count, no collapsed duplicates, and
that it still matches the C# — without opening Unity. Edit the C# first, then mirror
the constants here; --check fails if the two drift apart.

    python3 tools/palette_preview.py            # write palette-preview.png
    python3 tools/palette_preview.py --check    # validate only, no image
"""
import argparse
import math
import os
import sys

# --- Palette definition — mirrors PastelPalette.cs ---------------------------

HUE_COUNT = 16

# Lightness steps, palest first.
LIGHTNESSES = [0.92, 0.82, 0.72, 0.61, 0.49, 0.36]

# Chroma is a real axis: a muted and a vivid variant per hue and lightness, given as
# fractions of the in-gamut ceiling rather than absolute chroma (sRGB holds wildly
# different chroma per hue, so a fixed pair would collapse into duplicates at some).
CHROMA_FRACTIONS = [0.5, 1.0]
CHROMA_CEILING = 0.20

# The neutral ramp is pure grey — chroma zero — running black-ish up to near white.
NEUTRAL_COUNT = 12
NEUTRAL_MIN_L = 0.16
NEUTRAL_MAX_L = 0.97


# --- Oklch <-> sRGB ----------------------------------------------------------

def oklch_to_linear(lightness, chroma, hue_radians):
    a = chroma * math.cos(hue_radians)
    b = chroma * math.sin(hue_radians)
    l = (lightness + 0.3963377774 * a + 0.2158037573 * b) ** 3
    m = (lightness - 0.1055613458 * a - 0.0638541728 * b) ** 3
    s = (lightness - 0.0894841775 * a - 1.2914855480 * b) ** 3
    return (+4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
            -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s)


def linear_to_gamma(c):
    """Unity's Color.gamma."""
    if c <= 0.0:
        return 0.0
    if c <= 0.0031308:
        return c * 12.92
    if c < 1.0:
        return 1.055 * (c ** 0.41666) - 0.055
    return 1.0


def in_gamut(lightness, chroma, hue_radians, eps=1e-4):
    return all(-eps <= c <= 1.0 + eps
               for c in oklch_to_linear(lightness, chroma, hue_radians))


def fit_chroma(lightness, chroma, hue_radians, iterations=16):
    """Largest chroma up to `chroma` that stays inside sRGB.

    Clamping an out-of-gamut colour instead shifts its hue and drops its lightness,
    so an authored entry quietly comes out darker and dirtier than the ramp says.
    sRGB holds far less chroma in blue than in yellow, so a flat chroma ramp goes
    out of gamut at some hues and not others.
    """
    if in_gamut(lightness, chroma, hue_radians):
        return chroma
    low, high = 0.0, chroma
    for _ in range(iterations):
        mid = 0.5 * (low + high)
        if in_gamut(lightness, mid, hue_radians):
            low = mid
        else:
            high = mid
    return low


def oklch_to_srgb(lightness, chroma, hue_radians):
    chroma = fit_chroma(lightness, chroma, hue_radians)
    return tuple(linear_to_gamma(max(0.0, min(1.0, c)))
                 for c in oklch_to_linear(lightness, chroma, hue_radians))


def lerp(a, b, t):
    return a + (b - a) * t


def default_palette():
    """The lattice, in the order PastelPalette.Default() emits it."""
    colors = []
    for h in range(HUE_COUNT):
        hue = h * (2.0 * math.pi / HUE_COUNT)
        for lightness in LIGHTNESSES:
            # fit_chroma of the ceiling IS the in-gamut maximum.
            ceiling = fit_chroma(lightness, CHROMA_CEILING, hue)
            for fraction in CHROMA_FRACTIONS:
                colors.append(oklch_to_srgb(lightness, ceiling * fraction, hue))

    for n in range(NEUTRAL_COUNT):
        lightness = lerp(NEUTRAL_MIN_L, NEUTRAL_MAX_L, n / (NEUTRAL_COUNT - 1.0))
        colors.append(oklch_to_srgb(lightness, 0.0, 0.0))

    return colors


# --- Checking ----------------------------------------------------------------

CSHARP = os.path.join('Assets', 'Game', 'Scripts', 'World', 'Environment', 'ColorGrade',
                      'PastelPalette.cs')


def check_mirrors_csharp(repo_root):
    """Re-reads the constants out of PastelPalette.cs and compares.

    Without this the port silently rots the moment someone tunes the C# ramp, and the
    contact sheet starts showing a palette the game does not use.
    """
    import re

    path = os.path.join(repo_root, CSHARP)
    try:
        with open(path) as handle:
            source = handle.read()
    except OSError as error:
        return ['cannot read %s: %s' % (CSHARP, error)]

    problems = []

    def scalar(name, expected):
        match = re.search(r'\b%s\s*=\s*([0-9.]+)f?\s*;' % name, source)
        if match is None:
            problems.append('%s: no %s found' % (CSHARP, name))
        elif abs(float(match.group(1)) - expected) > 1e-6:
            problems.append('%s: %s is %s in C#, %s here'
                            % (CSHARP, name, match.group(1), expected))

    def float_array(name, expected):
        match = re.search(r'%s\s*=\s*\{([^}]*)\}' % name, source, re.S)
        if match is None:
            problems.append('%s: no %s array found' % (CSHARP, name))
            return
        found = [float(v) for v in re.findall(r'([0-9.]+)f', match.group(1))]
        if [round(v, 6) for v in found] != [round(v, 6) for v in expected]:
            problems.append('%s: %s differs from this port\n'
                            '    C#: %s\n    here: %s' % (CSHARP, name, found, expected))

    float_array('Lightnesses', LIGHTNESSES)
    float_array('ChromaFractions', CHROMA_FRACTIONS)
    scalar('ChromaCeiling', CHROMA_CEILING)

    scalar('HueCount', HUE_COUNT)
    scalar('NeutralCount', NEUTRAL_COUNT)
    scalar('NeutralMinL', NEUTRAL_MIN_L)
    scalar('NeutralMaxL', NEUTRAL_MAX_L)

    return problems


def hsv_saturation_value(color):
    high, low = max(color), min(color)
    return (0.0 if high == 0.0 else (high - low) / high), high


def check(colors):
    """Returns a list of complaints; empty means the palette is well formed.

    Deliberately does NOT enforce a pastel-only rule: this palette runs down to dark
    muted steps and its neutral ramp is pure grey, both on purpose.
    """
    problems = []
    expected = HUE_COUNT * len(LIGHTNESSES) * len(CHROMA_FRACTIONS) + NEUTRAL_COUNT
    if len(colors) != expected:
        problems.append('expected %d entries, built %d' % (expected, len(colors)))

    seen = {}
    for i, color in enumerate(colors):
        name = '#%02X%02X%02X' % tuple(round(c * 255) for c in color)
        if name in seen:
            problems.append('entry %d %s duplicates entry %d — a collapsed entry is a '
                            'wasted palette slot' % (i, name, seen[name]))
        seen[name] = i

    return problems


# --- Contact sheet -----------------------------------------------------------

SWATCH_W = 56
SWATCH_H = 40


def render(colors, path):
    from PIL import Image

    columns = len(LIGHTNESSES) * len(CHROMA_FRACTIONS)
    hue_rows = HUE_COUNT
    neutral_rows = -(-NEUTRAL_COUNT // columns)  # ceil
    gap_rows = 1
    rows = hue_rows + gap_rows + neutral_rows

    image = Image.new('RGB', (columns * SWATCH_W, rows * SWATCH_H), (255, 255, 255))
    pixels = image.load()

    def paint(row, column, color):
        rgb = tuple(round(c * 255) for c in color)
        for y in range(row * SWATCH_H, (row + 1) * SWATCH_H):
            for x in range(column * SWATCH_W, (column + 1) * SWATCH_W):
                pixels[x, y] = rgb

    for h in range(hue_rows):
        for c in range(columns):
            paint(h, c, colors[h * columns + c])

    first_neutral = HUE_COUNT * columns
    for n in range(NEUTRAL_COUNT):
        row = hue_rows + gap_rows + n // columns
        paint(row, n % columns, colors[first_neutral + n])

    image.save(path)
    return image.size


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true',
                        help='validate the palette without writing the image')
    parser.add_argument('--out', default=None, help='output path for the contact sheet')
    args = parser.parse_args()

    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    colors = default_palette()
    problems = check_mirrors_csharp(repo_root) + check(colors)

    print('%d colours' % len(colors))
    if problems:
        for problem in problems:
            print('  FAIL %s' % problem)
        return 1
    print('  matches %s' % CSHARP)
    print('  %d distinct, no collapsed entries' % len(set(colors)))

    if args.check:
        return 0

    out = args.out or os.path.join(repo_root, 'palette-preview.png')
    size = render(colors, out)
    print('  wrote %s (%dx%d)' % (out, size[0], size[1]))
    return 0


if __name__ == '__main__':
    sys.exit(main())
