"""Gauntlet shell — the open frame that puts a machine on the back of a hand.

The parts a hand-worn powered tool is dressed in, separated from the mechanism that does
the work: a frame the hand goes *through*, a hazard-striped shield, a wrist collar, and a
small pressure vessel that feeds it.

## This is a frame, not a housing

The first version of this file was a brass carcass — bulkhead, floor, two cheeks, front
lip — and a hand could not get into it. Not "it was tight": the bulkhead was a solid slab
across the entire wrist end, commented "the wall the arm passes through", with no opening
ever cut in it.

So the rule this file is built to now: **the hand is a volume, and no geometry may enter
it.** `CAVITY` below is that volume, every builder is written around it, and
`assert_clear()` proves at build time that nothing intrudes. A frame you can see the hand
through is also what the concept art shows and what reads as a machine rather than a brick.

## Scale — world metres, measured off the rig

Authored 1:1 with the game. The astronaut's right hand, from
`Tools/SpaceGame/Items/Audit Held Item Poses`:

    wrist -> knuckles   0.176 m
    knuckle span        0.113 m
    knuckle -> fingertip 0.099 m
    thumb base off palm 0.082 m

That is a big hand — roughly 1.7x human — so a gauntlet drawn to human proportions is far
too small for it, which is exactly what the first version was. The model ships with
`ItemGrip.holdSize` set to its own longest axis so world scale stays pinned at 1.0 and
these numbers keep meaning what they say.

## Axes and origin

**-Y forward (fingers), +Z is the back of the hand.** All four parts share an origin at the
**grip point** — the centre of the bar the fingers close on, which is where
`HandGripFrame` seats the item. So `(0, 0, 0)` is a real anatomical landmark, and the
wrist and knuckles sit at known offsets from it:

    wrist bone     y = +0.079   (handLength * GripDepthAlongFingers, from HandGripFrame)
    knuckle row    y = -0.097
    back of hand   z = +0.066

`Boiler` is the exception and is documented at its own function.

## Hazard stripes are geometry, not texture

The library ships untextured meshes with per-face palette materials, so the striped plate
is built by clipping 45-degree bands against the plate outline and extruding each a
fraction of a millimetre proud. It holds up in silhouette and costs ~300 triangles.

Generation script — historical record. The .blend is the source of truth; never re-run this
over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "mechanical"))

from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402
from panel_control import tube_path  # noqa: E402

# Index 0 is STEEL: `bmesh.ops.bevel` stamps its new faces with material index 0, so an
# accent colour there would paint every chamfer in the file.
STEEL, DARK, BRASS, YELLOW, BLACK, CHROME, RUBBER, RED, AMBER = range(9)
MATS = ["Mat_Metal_Steel_Worn",       # the frame members
        "Mat_Metal_Steel_Dark",       # recesses, bosses, shadowed insides
        "Mat_Metal_Brass_Tarnished",  # the back plate — the warm mass of the thing
        "Mat_Paint_Hazard_Yellow",    # the shield
        "Mat_Neutral_Black_Matte",    # the stripes on it
        "Mat_Metal_Chrome_Scuffed",   # fasteners
        "Mat_Plastic_Rubber_Black",   # grip wrap, straps, hoses
        "Mat_Paint_Warn_Red",         # one danger band
        "Mat_Emissive_Amber"]         # the pressure gauge face

BEVEL_W = 0.0014

# ── The hand. Nothing may be built inside this box. ────────────────────────────
#
# Derived from the bone landmarks, which are the numbers that reconcile with Unity
# (handLength 0.176 measured in Blender matches the audit's 0.176 exactly). The mesh's own
# bounds are NOT usable here: this rig has known weighting problems, stray vertices carry
# >0.5 weight on a hand bone while sitting out on the forearm, and even a 2nd-98th
# percentile box comes out 0.42 x 0.29 — a hand that would be half a metre long.
#
#   knuckle span (bone centres)  0.113  ->  hand across the flesh ~0.130
#   half-width + 7 mm clearance          ->  0.072
#   hand thickness through the palm      ->  ~0.062, and the grip point sits at the palm
#                                            side of it, so the back of the hand is +0.066
CAVITY = {
    "x": (-0.072, 0.072),
    "y": (-0.108, 0.092),   # knuckles exit at -0.097, wrist enters at +0.079
    "z": (-0.038, 0.066),   # curled fingers below the bar, back of the hand above
}

# The one thing allowed inside the cavity: the bar the fingers close around. A hand
# gripping a bar means the bar is inside the hand's own bounding box, so a flat "nothing
# in the cavity" rule would forbid the handle along with the mistakes.
HANDLE = {"y": (-0.032, 0.026), "z": (-0.032, 0.024)}

WRIST_Y = 0.079
KNUCKLE_Y = -0.097
BACK_Z = CAVITY["z"][1]     # the plane the frame's spine sits on
RAIL_X = 0.079              # side rails, outboard of the cavity by their own radius
DECK_Z = 0.082              # top of the back plate — the mechanism's mounting plane


def assert_clear(obj, name):
    """Refuse to ship a part that reaches into the hand.

    Cheap, and it is the one mistake this component has already made once. Checked on the
    emitted mesh rather than on the source numbers, so a slab written correctly and then
    bevelled or mirrored into the cavity is still caught.
    """
    bad = []
    for v in obj.data.vertices:
        p = obj.matrix_world @ v.co
        if not all(CAVITY[a][0] < c < CAVITY[a][1]
                   for a, c in zip("xyz", (p.x, p.y, p.z))):
            continue
        if all(HANDLE[a][0] < c < HANDLE[a][1] for a, c in zip("yz", (p.y, p.z))):
            continue  # the grip bar, which is meant to be in the hand's grasp
        bad.append(p)
    if bad:
        raise SystemExit(
            "%s puts %d vertex/vertices inside the hand cavity, e.g. (%.3f, %.3f, %.3f). "
            "The hand has to fit." % (name, len(bad), *bad[0]))
    print("  %-34s clear of the hand cavity" % name)
    return obj


# ---------------------------------------------------------------------------
# Hazard striping — convex clipping in the plate's own plane
# ---------------------------------------------------------------------------

def _clip(poly, nx, ny, d, sign):
    """Sutherland-Hodgman: keep the part of `poly` where sign*(n.p - d) >= 0."""
    def f(pt):
        return sign * (nx * pt[0] + ny * pt[1] - d)

    out = []
    for i in range(len(poly)):
        a, b = poly[i], poly[(i + 1) % len(poly)]
        fa, fb = f(a), f(b)
        if fa >= 0:
            out.append(a)
        if (fa > 0) != (fb > 0):
            t = fa / (fa - fb)
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))

    # Drop points the clip collapsed onto each other, or `prism` builds a face with a
    # zero-length edge and the normals come out inconsistent.
    clean = []
    for pt in out:
        if not clean or math.dist(pt, clean[-1]) > 1e-6:
            clean.append(pt)
    if len(clean) > 1 and math.dist(clean[0], clean[-1]) <= 1e-6:
        clean.pop()
    return clean


def _area(poly):
    s = 0.0
    for i in range(len(poly)):
        x0, y0 = poly[i]
        x1, y1 = poly[(i + 1) % len(poly)]
        s += x0 * y1 - x1 * y0
    return abs(s) * 0.5


def hazard_stripes(p, rect, z, proud=0.0012, pitch=0.030, angle=45.0, mat=BLACK):
    """Diagonal bands clipped to `rect`, standing `proud` of a plate at `z`.

    Half of each `pitch` is painted, which is what makes it read as hazard striping rather
    than as a grille.
    """
    x0, y0, x1, y1 = rect
    corners = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    a = math.radians(angle)
    nx, ny = -math.sin(a), math.cos(a)

    proj = [nx * cx + ny * cy for cx, cy in corners]
    k0 = int(math.floor(min(proj) / pitch)) - 1
    k1 = int(math.ceil(max(proj) / pitch)) + 1

    faces = []
    for k in range(k0, k1 + 1):
        lo = k * pitch
        band = _clip(_clip(corners, nx, ny, lo, 1.0),
                     nx, ny, lo + pitch * 0.5, -1.0)
        if len(band) < 3 or _area(band) < 1e-6:
            continue
        faces += p.prism(band, proud, axis='Z', mat=mat,
                         offset=(0, 0, z + proud / 2))
    return faces


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def frame(coll, mats):
    """The open frame: a spine over the back of the hand on two side rails.

    Five members and nothing else — back plate, two rails, a knuckle bridge, a wrist yoke —
    plus the bar the fingers close on. Everything below and between them is air, which is
    both what makes the hand fit and what makes the piece read as a mechanism.
    """
    p = TrackedPart(mats)
    rail_r = 0.0065
    out_x = RAIL_X + 0.007        # the frame's outer skin, clear of the rails

    # ── Spine: the brass plate over the back of the hand ──
    hard = p.slab((-0.064, KNUCKLE_Y - 0.014, BACK_Z + 0.002),
                  (0.064, WRIST_Y - 0.004, DECK_Z), BRASS)
    # A raised rib down the centre, so 17 cm of flat brass has a highlight to catch.
    hard += p.slab((-0.015, KNUCKLE_Y - 0.008, DECK_Z),
                   (0.015, WRIST_Y - 0.012, DECK_Z + 0.006), BRASS)
    hard += p.slab((-0.054, -0.034, DECK_Z), (0.054, -0.022, DECK_Z + 0.003), RED)
    # Lightening slots through the spine: the cheapest way to say "made of plate".
    for y in (-0.062, -0.014, 0.034):
        p.slab((-0.040, y - 0.008, DECK_Z - 0.002), (0.040, y + 0.008, DECK_Z + 0.001),
               DARK)

    for sx in (-1, 1):
        # ── Side rails, running the length of the hand just outside the cavity ──
        p.cyl((sx * RAIL_X, (KNUCKLE_Y + WRIST_Y) / 2 - 0.006, 0.006),
              rail_r, WRIST_Y - KNUCKLE_Y + 0.010, 'Y', 8, STEEL)

        # Uprights tying each rail to the spine. Three, not a solid cheek: the gaps
        # between them are where the hand shows through, which is the whole design.
        for y in (WRIST_Y - 0.024, -0.020, KNUCKLE_Y + 0.018):
            hard += p.box((sx * RAIL_X, y, (0.006 + DECK_Z) / 2),
                          (0.013, 0.018, DECK_Z - 0.006), STEEL)
            p.cyl((sx * (RAIL_X + 0.005), y, 0.006), 0.0044, 0.010, 'X', 6, CHROME)

    # ── Knuckle bridge: the front wall, and what a striking head mounts through ──
    hard += p.slab((-out_x, KNUCKLE_Y - 0.025, -0.012),
                   (out_x, KNUCKLE_Y - 0.011, DECK_Z + 0.004), STEEL)
    hard += p.slab((-0.062, KNUCKLE_Y - 0.031, 0.002),
                   (0.062, KNUCKLE_Y - 0.025, DECK_Z - 0.008), DARK)
    for sx in (-1, 1):
        p.cyl((sx * 0.056, KNUCKLE_Y - 0.033, 0.028), 0.0058, 0.010, 'Y', 6, CHROME)

    # ── Wrist yoke: a C, open at the bottom, so the wrist drops in ──
    for sx in (-1, 1):
        hard += p.box((sx * (RAIL_X + 0.003), WRIST_Y + 0.008, 0.020),
                      (0.014, 0.026, 0.084), STEEL)
    hard += p.slab((-out_x, WRIST_Y - 0.003, BACK_Z), (out_x, WRIST_Y + 0.021, DECK_Z),
                   STEEL)
    # Strap across the underside — soft, so it closes the C without blocking entry.
    tube_path(p, [(-(RAIL_X + 0.003), WRIST_Y + 0.008, -0.026),
                  (0.0, WRIST_Y + 0.008, -0.054),
                  (RAIL_X + 0.003, WRIST_Y + 0.008, -0.026)], 0.0055, RUBBER, seg=6)

    # ── The bar the fingers close on ──
    # On the origin, because the origin IS the grip point: HandGripFrame seats the item so
    # that this point lands where a handle's axis would sit in the closed hand.
    p.cyl((0.0, -0.004, -0.004), 0.0140, 0.112, 'X', 12, RUBBER)
    for sx in (-1, 1):
        p.cyl((sx * 0.060, -0.004, -0.004), 0.0108, 0.014, 'X', 10, DARK)
        # Its stanchion, hugging the rail so it never crosses the palm.
        hard += p.box((sx * RAIL_X, -0.004, 0.001), (0.013, 0.020, 0.050), DARK)

    p.restamp("frame")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return assert_clear(p.finish("Mesh_GauntletShell_Frame", coll),
                        "Mesh_GauntletShell_Frame")


def hazard_plate(coll, mats):
    """The striped guard, carried on two side brackets over the mechanism.

    ## Why it is bracketed from the sides and not footed underneath

    A guard over a linear mechanism has nowhere to put a leg. Legs spaced along the plate —
    the obvious first choice, and what this file did originally — stand inside the
    carriage's stroke, and the ram drives through its own guard. Moving them to the ends
    does not help either: the far end is exactly where the ram arm ends up at full
    extension.

    So the load goes out to `BRACKET_X`, outboard of everything that moves (the carriage
    reaches x 0.064, the ram arm never comes back this far along Y) and down onto the
    frame's own side uprights. The plate is then a cantilever over the mechanism with
    nothing beneath it, which is also how a real machine guard is built.

    `PLATE_Z` clears the carriage's top (0.124 once mounted) rather than the deck.
    """
    p = TrackedPart(mats)
    x0, x1 = -0.076, 0.076
    y0, y1 = -0.190, 0.010
    BRACKET_X = 0.076
    BRACKET_Y = (-0.020, -0.079)     # over the frame's middle and front uprights
    z = 0.144
    thick = 0.006

    hard = p.slab((x0, y0, z), (x1, y1, z + thick), YELLOW)

    # A plain yellow border frames the striped field — every real hazard panel has one, and
    # without it the stripes run off the edge and read as noise.
    hazard_stripes(p, (x0 + 0.011, y0 + 0.011, x1 - 0.011, y1 - 0.011), z + thick)

    # Folded front lip, angled down over the knuckles.
    hard += p.slab((x0, y0 - 0.014, z - 0.010), (x1, y0, z + thick), YELLOW)

    for sx in (-1, 1):
        for y in BRACKET_Y:
            hard += p.box((sx * BRACKET_X, y, (DECK_Z + z) / 2 + 0.001),
                          (0.014, 0.022, z - DECK_Z + 0.004), STEEL)
            p.cyl((sx * BRACKET_X, y, z + thick + 0.002), 0.0050, 0.006, 'Z', 6, CHROME,
                  radius_top=0.0038)

    # Rolled edge beads: a flat plate has no thickness at a glancing angle, which is
    # exactly the angle this is seen from.
    for sx in (-1, 1):
        p.cyl((sx * (x1 - 0.002), (y0 + y1) / 2, z + thick), 0.0032, y1 - y0, 'Y', 6,
              STEEL)

    p.restamp("hazard_plate")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return assert_clear(p.finish("Mesh_GauntletShell_HazardPlate", coll),
                        "Mesh_GauntletShell_HazardPlate")


def wrist_collar(coll, mats):
    """Where the frame meets the forearm: a ring, a gasket and two latches.

    Sits entirely behind the wrist opening, so it never narrows the way in.
    """
    p = TrackedPart(mats)
    y = WRIST_Y + 0.048

    p.tube((0.0, y, 0.020), 0.070, 0.009, 0.030, 'Y', 16, STEEL)
    p.tube((0.0, y + 0.014, 0.020), 0.062, 0.006, 0.016, 'Y', 16, RUBBER)
    p.tube((0.0, y - 0.014, 0.020), 0.074, 0.007, 0.010, 'Y', 16, BRASS)

    hard = []
    for sx in (-1, 1):
        hard += p.box((sx * 0.072, y + 0.004, 0.020), (0.014, 0.034, 0.026), DARK)
        hard += p.box((sx * 0.080, y + 0.004, 0.020), (0.006, 0.044, 0.014), STEEL)
        p.cyl((sx * 0.072, y - 0.012, 0.020), 0.0048, 0.020, 'Y', 8, CHROME)

    p.restamp("wrist_collar")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return assert_clear(p.finish("Mesh_GauntletShell_WristCollar", coll),
                        "Mesh_GauntletShell_WristCollar")


def boiler(coll, mats):
    """The pressure vessel that feeds the mechanism.

    **Origin is the underside of its saddle**, not the grip point the other three share:
    this straps onto a forearm some way back, and an origin at the clamp is what lets it be
    slid along the arm without recomputing anything.
    """
    p = TrackedPart(mats)
    r = 0.026
    axis_z = 0.036
    half_len = 0.052

    p.cyl((0.0, 0.0, axis_z), r, half_len * 2, 'Y', 14, BRASS)
    # Dished ends rather than flat caps — a flat-ended cylinder is a tin can.
    for sy in (-1, 1):
        p.cyl((0.0, sy * (half_len + 0.005), axis_z), r, 0.011, 'Y', 14, BRASS,
              radius_top=r * 0.72)
        p.cyl((0.0, sy * (half_len + 0.012), axis_z), r * 0.72, 0.006, 'Y', 12, DARK)

    for y in (-0.024, 0.024):
        p.cyl((0.0, y, axis_z), r * 1.06, 0.008, 'Y', 14, STEEL)

    # Pressure gauge on the forward dome.
    p.cyl((0.0, -(half_len + 0.016), axis_z), 0.015, 0.010, 'Y', 12, CHROME)
    p.cyl((0.0, -(half_len + 0.022), axis_z), 0.0116, 0.004, 'Y', 12, AMBER)

    # Relief valve and the delivery line leaving the top.
    p.cyl((0.0, 0.010, axis_z + r), 0.008, 0.020, 'Z', 8, BRASS)
    p.cyl((0.0, 0.010, axis_z + r + 0.016), 0.012, 0.005, 'Z', 8, STEEL)
    tube_path(p, [(0.0, -0.020, axis_z + r + 0.002),
                  (0.0, -0.056, axis_z + r * 0.7),
                  (0.0, -0.084, axis_z * 0.6)], 0.0048, RUBBER, seg=6)

    hard = []
    for y in (-0.032, 0.032):
        hard += p.box((0.0, y, 0.006), (0.056, 0.012, 0.012), DARK)
        hard += p.box((0.0, y, 0.014), (0.014, 0.014, 0.022), STEEL)

    p.restamp("boiler")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_GauntletShell_Boiler", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    frame(collection("Coll_GauntletShell_Frame"), mats)
    hazard_plate(collection("Coll_GauntletShell_HazardPlate"), mats)
    wrist_collar(collection("Coll_GauntletShell_WristCollar"), mats)
    boiler(collection("Coll_GauntletShell_Boiler"), mats)

    save(out)
    report()


if __name__ == "__main__":
    main()
