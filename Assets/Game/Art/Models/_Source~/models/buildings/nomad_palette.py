"""The nomad settlement's colour map: eight roles, one table, any scheme.

The kit's eight materials are not eight colours, they are eight *jobs*. What
makes the town read is not the particular sand or terracotta but the ladder
between them: the footing is lighter than the wall it carries, the banding is
markedly darker than the wall it rings, the vents are near-black, the panes are
the brightest thing on the building. Change every hex and the town still reads,
as long as that ladder survives.

So the colours live here, keyed by role, and nowhere else. A retune is a new
entry in `SCHEMES` - eight hexes - and `check` refuses one that breaks the
ladder before it ever reaches a .blend.

    # what the roles are, and what carries each
    blender --background --python nomad_palette.py -- --list

    # does a scheme keep the contrast system?
    blender --background --python nomad_palette.py -- --check ash

    # what is actually in a file right now
    blender --background <file>.blend --python nomad_palette.py -- --dump

    # retune a file in place
    blender --background <file>.blend --python nomad_palette.py -- \
        --apply ash --save

`apply` touches material colours only - never geometry, never a material that
is not in the map - so it is safe on hand-edited files.
"""

import sys
from collections import OrderedDict

try:
    import bpy
except ImportError:                                    # importable outside Blender
    bpy = None


# --------------------------------------------------------------- the roles
#
# `material` is the datablock name in the kit. `carries` is measured off the
# finished settlement, not guessed - it is what actually wears the colour.
# `roughness` and `metallic` are part of the *system*, not of a scheme: the
# panes are the only smooth surface in the kit, and nothing in an adobe town is
# metallic. A scheme may override them, but it has to say so.
ROLES = OrderedDict([
    ("wall", dict(
        material="Mat_Nomad_Clay_Sand", roughness=0.50, metallic=0.0,
        carries="drums, block faces, meter-box bodies - the town's base colour, "
                "on more parts than any other role")),
    ("footing", dict(
        material="Mat_Nomad_Clay_Bone", roughness=0.50, metallic=0.0,
        carries="R12's ground block on every round building - the stone course "
                "the adobe stands on, and the only role that has to read "
                "lighter than the wall above it")),
    ("joinery", dict(
        material="Mat_Nomad_Clay_Ochre", roughness=0.50, metallic=0.0,
        carries="door leaves, rect blocks, foundations, simple roofs - the "
                "worked timber-and-plaster tone, a step down from the wall")),
    ("trim", dict(
        material="Mat_Nomad_Clay_Terracotta", roughness=0.50, metallic=0.0,
        carries="rings and bands, door frames, doorsteps, roof decks, stacks - "
                "the banding that makes storeys read as storeys")),
    ("shadow", dict(
        material="Mat_Nomad_Clay_Oxblood", roughness=0.50, metallic=0.0,
        carries="vent slots, recesses, deep detail - the near-black that gives "
                "a flat facade depth at distance")),
    ("pipe", dict(
        material="Mat_Nomad_Metal_Charcoal", roughness=0.50, metallic=0.0,
        carries="generated pipe runs and their clamps - service gear, read as "
                "bolted on rather than built in")),
    ("fitting", dict(
        material="Mat_Nomad_Metal_Grey", roughness=0.50, metallic=0.0,
        carries="collars, brackets, masts, dish, and any kit part the file left "
                "material-less")),
    ("glass", dict(
        material="Mat_Nomad_Glass_Amber", roughness=0.27, metallic=0.0,
        carries="every window pane - the brightest thing on a building and the "
                "only one that reads as lit")),
])


# ----------------------------------------------------------- the schemes
#
# sRGB hex, the way a human tunes a colour. Converted to linear on apply,
# because that is what a Principled base colour wants.
SCHEMES = {
    # What the settlement ships with. `footing` is the user's hand edit -
    # the bone course went yellow - and this table is where that now lives,
    # so a rebuild keeps it.
    "nomad": dict(
        wall="DFC196", footing="FFC570", joinery="DFB38A", trim="B87D5B",
        shadow="624535", pipe="525252", fitting="969696", glass="E7E18F"),

    # Cold volcanic grit: same ladder, no warmth in it at all.
    "ash": dict(
        wall="C3C8CE", footing="D2D8DE", joinery="AEB6BE", trim="6E7A86",
        shadow="2B3138", pipe="3A3F45", fitting="7E858D", glass="D8EAF4"),

    # Oxidised copper over pale bleached clay.
    "verdigris": dict(
        wall="CDC8A8", footing="E8E2BC", joinery="B4B38A", trim="5F7A63",
        shadow="222C23", pipe="414E46", fitting="7C8A7B", glass="CFEBD6"),
}

DEFAULT_SCHEME = "nomad"


# ------------------------------------------------------- the contrast system
#
# The part of the look that is NOT free to change. Each row is "this role must
# read lighter than that one, by at least this much relative luminance".
# Thresholds sit under the shipped scheme's real gaps, so `nomad` passes with
# headroom and a retune has room to move without flattening the town.
CONTRASTS = [
    ("glass", "wall", 0.10, "panes are the brightest thing on a building"),
    ("footing", "wall", 0.04, "the footing course reads lighter than the adobe"),
    ("wall", "joinery", 0.03, "worked surfaces sit a step below the wall"),
    ("joinery", "trim", 0.15, "banding is markedly darker, or storeys stop reading"),
    ("trim", "shadow", 0.12, "vents and recesses are the near-black"),
    ("fitting", "pipe", 0.12, "collars catch light the pipe run does not"),
    ("trim", "pipe", 0.08, "service gear is darker than the masonry banding"),
]

# Two roles this close in luminance are indistinguishable at settlement
# distance, which is how a palette quietly collapses to five colours.
MIN_SEPARATION = 0.012


# ----------------------------------------------------------------- colour
def hex_to_srgb(h):
    h = h.strip().lstrip("#")
    if len(h) != 6:
        raise ValueError("hex must be 6 digits, got %r" % h)
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def linear_to_srgb(c):
    return c * 12.92 if c <= 0.0031308 else 1.055 * (c ** (1 / 2.4)) - 0.055


def hex_to_linear(h):
    return tuple(srgb_to_linear(c) for c in hex_to_srgb(h))


def linear_to_hex(rgb):
    return "".join("%02X" % round(max(0.0, min(1.0, linear_to_srgb(c))) * 255)
                   for c in rgb[:3])


def luminance(rgb_linear):
    r, g, b = rgb_linear[:3]
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def scheme_colours(name):
    """Resolve a scheme to {role: (linear rgb, roughness, metallic)}."""
    if name not in SCHEMES:
        raise KeyError("no scheme %r - have %s"
                       % (name, ", ".join(sorted(SCHEMES))))
    s = SCHEMES[name]
    missing = [r for r in ROLES if r not in s]
    if missing:
        raise KeyError("scheme %r is missing roles: %s"
                       % (name, ", ".join(missing)))
    out = OrderedDict()
    for role, spec in ROLES.items():
        v = s[role]
        if isinstance(v, dict):
            rgb = hex_to_linear(v["hex"])
            rough = v.get("roughness", spec["roughness"])
            metal = v.get("metallic", spec["metallic"])
        else:
            rgb, rough, metal = hex_to_linear(v), spec["roughness"], spec["metallic"]
        out[role] = (rgb, rough, metal)
    return out


# ------------------------------------------------------------------ checks
def check(name):
    """Complaints about a scheme, as a list. Empty means the ladder holds."""
    cols = scheme_colours(name)
    lum = {role: luminance(rgb) for role, (rgb, _r, _m) in cols.items()}
    bad = []
    for brighter, darker, gap, why in CONTRASTS:
        d = lum[brighter] - lum[darker]
        if d < gap:
            bad.append("%s vs %s: gap %.3f, needs %.3f - %s"
                       % (brighter, darker, d, gap, why))
    ranked = sorted(lum.items(), key=lambda kv: -kv[1])
    for (ra, la), (rb, lb) in zip(ranked, ranked[1:]):
        if la - lb < MIN_SEPARATION:
            bad.append("%s and %s are %.4f apart - indistinguishable at distance"
                       % (ra, rb, la - lb))
    return bad


def ladder(name):
    """The scheme's roles, brightest first, with their luminance."""
    cols = scheme_colours(name)
    rows = [(r, linear_to_hex(c[0]), luminance(c[0])) for r, c in cols.items()]
    return sorted(rows, key=lambda t: -t[2])


# ------------------------------------------------------------------- apply
def _base_name(mat_name):
    """Fold Blender's duplicate suffix: `Mat_X.017` is still `Mat_X`.

    The kit ships eighty-odd materials because duplicating an object in the UI
    duplicates its material; they are byte-identical copies of the same eight.
    A retune has to reach all of them or half the town stays the old colour.
    """
    if len(mat_name) > 4 and mat_name[-4] == "." and mat_name[-3:].isdigit():
        return mat_name[:-4]
    return mat_name


def apply(name=DEFAULT_SCHEME, strict=True):
    """Paint a scheme onto whatever is loaded. Returns how many datablocks moved.

    Only materials the map names are touched, duplicates included. Anything else
    in the file - a hand-made one-off, a material from another kit - is left
    exactly as it was.
    """
    if bpy is None:
        raise RuntimeError("apply needs Blender")
    bad = check(name)
    if bad and strict:
        raise ValueError("scheme %r breaks the contrast system:\n  %s"
                         % (name, "\n  ".join(bad)))
    cols = scheme_colours(name)
    by_material = {ROLES[r]["material"]: (r,) + cols[r] for r in ROLES}

    touched, seen = 0, set()
    for m in bpy.data.materials:
        spec = by_material.get(_base_name(m.name))
        if spec is None:
            continue
        role, rgb, rough, metal = spec
        seen.add(role)
        rgba = (rgb[0], rgb[1], rgb[2], 1.0)
        # The viewport's solid mode reads `diffuse_color`, which the kit never
        # set - so solid mode showed the whole town in default grey. Set both,
        # and the material preview and the solid view finally agree.
        m.diffuse_color = rgba
        m.roughness = rough
        m.metallic = metal
        if m.use_nodes and m.node_tree:
            for n in m.node_tree.nodes:
                if n.type == 'BSDF_PRINCIPLED':
                    n.inputs['Base Color'].default_value = rgba
                    n.inputs['Roughness'].default_value = rough
                    n.inputs['Metallic'].default_value = metal
        touched += 1

    absent = [r for r in ROLES if r not in seen]
    if absent:
        print("[palette] not in this file, left alone: %s" % ", ".join(absent))
    return touched


def dump():
    """What the loaded file actually carries, role by role."""
    if bpy is None:
        raise RuntimeError("dump needs Blender")
    by_material = {ROLES[r]["material"]: r for r in ROLES}
    found = {}
    for m in bpy.data.materials:
        role = by_material.get(_base_name(m.name))
        if role is None or role in found:
            continue
        rgb = None
        if m.use_nodes and m.node_tree:
            for n in m.node_tree.nodes:
                if n.type == 'BSDF_PRINCIPLED':
                    rgb = tuple(n.inputs['Base Color'].default_value)[:3]
        if rgb is not None:
            found[role] = rgb
    rows = [(r, linear_to_hex(c), luminance(c)) for r, c in found.items()]
    return sorted(rows, key=lambda t: -t[2])


# --------------------------------------------------------------------- cli
def _argv():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def main():
    a = _argv()
    if "--list" in a:
        print("roles, in the order they were measured off the settlement:\n")
        for role, spec in ROLES.items():
            print("  %-8s %-30s %s" % (role, spec["material"], spec["carries"]))
        print("\ncontrast system - what a retune may not break:\n")
        for brighter, darker, gap, why in CONTRASTS:
            print("  %-8s > %-8s by %.2f   %s" % (brighter, darker, gap, why))
        print("\nschemes: %s" % ", ".join(sorted(SCHEMES)))
        return

    if "--check" in a:
        i = a.index("--check")
        names = [a[i + 1]] if len(a) > i + 1 and not a[i + 1].startswith("-") \
            else sorted(SCHEMES)
        rc = 0
        for n in names:
            bad = check(n)
            print("\n%s: %s" % (n, "OK" if not bad else "BROKEN"))
            for role, hexv, lum in ladder(n):
                print("    %-8s #%s  Y=%.3f" % (role, hexv, lum))
            for b in bad:
                print("    ! %s" % b)
            rc |= bool(bad)
        sys.exit(rc)

    if "--dump" in a:
        for role, hexv, lum in dump():
            print("  %-8s #%s  Y=%.3f" % (role, hexv, lum))
        return

    if "--apply" in a:
        i = a.index("--apply")
        name = a[i + 1] if len(a) > i + 1 else DEFAULT_SCHEME
        n = apply(name, strict="--force" not in a)
        print("[palette] %r applied to %d material datablocks" % (name, n))
        if "--save" in a:
            bpy.ops.wm.save_mainfile()
            print("[palette] saved %s" % bpy.data.filepath)
        return

    print(__doc__)


if __name__ == "__main__":
    main()
