"""components/mechanical/claw_chela — the grabbing head on the end of a tail.

Unlike most components in this library each variation is **several objects**,
not one: a palm plus the jaws that move on it. A claw whose jaws are merged into
the palm mesh cannot open, and the whole point of putting it on the crawler is
that it picks things up. `load_component` indexes every mesh in a collection and
`mesh_of(coll, contains=...)` selects between them, so the assembly can bone-
parent each jaw to its own bone.

Authoring frame: origin on the **wrist pivot**, geometry out along **+Y**, +Z
dorsal — the same convention the tail segments use, so a claw drops onto the end
of a tail with no reorientation. Each jaw's own origin sits on **its hinge pin**,
so rotating its bone opens it and nothing slides.

Rest pose is closed, tips meeting on the centreline. The assembly rotates the
jaw bones to whatever gape it wants rather than the mesh being authored ajar.

Three variations:

    Heavy    two opposed crusher pincers with interlocking teeth — the scorpion
             silhouette, and what a salvager needs to tear plate off a wreck
    Fine     two upper pickers opposing one thumb, with a sensor head between
    Cutter   fixed anvil under a powered shear blade, plus a cutting torch

    blender --background --python claw_chela.py -- --out claw_chela.blend
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Paint_Hull_Bleached",   # 0
    "Mat_Metal_Steel_Dark",      # 1
    "Mat_Metal_Steel_Worn",      # 2
    "Mat_Paint_Olive_Deep",      # 3
    "Mat_Paint_Roof_Green",      # 4
    "Mat_Metal_Rust_Heavy",      # 5
    "Mat_Paint_Warn_Red",        # 6
    "Mat_Emissive_Amber",        # 7
    "Mat_Neutral_Black_Matte",   # 8
    "Mat_Metal_Chrome_Scuffed",  # 9
    "Mat_Plastic_Rubber_Black",  # 10
    "Mat_Metal_Copper_Oxide",    # 11
    "Mat_Emissive_Green_CRT",    # 12
    "Mat_Glass_Canopy_Tinted",   # 13
]
(HULL, DARK, STEEL, OLIVE, GREEN, RUST, RED, AMBER, BLACK, CHROME, RUBBER,
 COPPER, CRT, GLASS) = range(14)

WRIST_R = 1.10       # matches tail_segment's nominal R, so the mate is 1:1
HINGE_Y = 1.85       # where the jaws pivot, measured from the wrist
HINGE_Z = 0.95       # half the separation between the two hinge pins


# ---------------------------------------------------------------------------
# Shared profile helpers
# ---------------------------------------------------------------------------

def rounded(hw, hh, corner=0.28, n_corner=3):
    """A rounded-rectangle profile in (u, v) — the jaw's cross-section."""
    c = min(corner, hw * 0.9, hh * 0.9)
    pts = []
    for cx, cz, a0 in ((hw - c, hh - c, 0.0), (-(hw - c), hh - c, 90.0),
                       (-(hw - c), -(hh - c), 180.0), (hw - c, -(hh - c), 270.0)):
        for i in range(n_corner + 1):
            a = math.radians(a0 + 90.0 * i / n_corner)
            pts.append((cx + c * math.cos(a), cz + c * math.sin(a)))
    return pts


def sweep(p, stations, mat, corner=0.28):
    """Loft a curved tapering horn.

    `stations` is [(y, dz, half_width, half_height), ...]. Offsetting each
    station's profile in v is what bends the sweep — `loft` runs along a
    straight axis, so the curve has to live in the profiles.
    """
    sections = []
    for y, dz, hw, hh in stations:
        prof = [(u, v + dz) for u, v in rounded(hw, hh, corner)]
        sections.append((y, prof))
    return p.loft(sections, axis='Y', mat=mat)


def ring_profile(r, n=14):
    return [(r * math.cos(2 * math.pi * i / n), r * math.sin(2 * math.pi * i / n))
            for i in range(n)]


# ---------------------------------------------------------------------------
# The wrist + palm shared by every variation
# ---------------------------------------------------------------------------

def build_wrist(p):
    """Roll bearing and the collar that mates onto the last tail segment."""
    p.tube((0, 0.10, 0), WRIST_R * 0.92, 0.16, 0.30, 'Y', 18, STEEL)
    p.tube((0, 0.34, 0), WRIST_R * 0.78, 0.13, 0.24, 'Y', 18, CHROME)
    p.loft([(0.44, ring_profile(WRIST_R * 0.80)),
            (0.95, ring_profile(WRIST_R * 0.86)),
            (1.45, ring_profile(WRIST_R * 0.72))], axis='Y', mat=DARK)
    # Roll drive: a ring of bolt heads plus the motor can slung to one side.
    for k in range(12):
        a = math.radians(k * 30)
        p.cyl((WRIST_R * 0.86 * math.cos(a), 0.24, WRIST_R * 0.86 * math.sin(a)),
              0.05, 0.10, 'Y', 6, CHROME)
    p.cyl((-WRIST_R * 0.72, 0.72, 0.30), 0.26, 0.62, 'Y', 12, OLIVE)
    p.cyl((-WRIST_R * 0.72, 0.72, 0.30), 0.10, 0.74, 'Y', 8, CHROME)
    # Hoses crossing the wrist into the palm.
    for dz, mat in ((0.42, RUBBER), (0.20, COPPER), (-0.34, RUBBER)):
        p.cyl((WRIST_R * 0.58, 0.80, dz), 0.06, 1.30, 'Y', 8, mat,
              rot=Matrix.Rotation(math.radians(7), 4, 'X'))


def build_palm(p, hw=1.05):
    """The housing the jaws hang off, and the rams that drive them."""
    sections = [(1.40, rounded(hw * 0.72, 0.80, 0.26)),
                (1.72, rounded(hw, 1.02, 0.30)),
                (2.15, rounded(hw * 0.96, 1.14, 0.30)),
                (2.42, rounded(hw * 0.66, 0.92, 0.24))]
    p.loft(sections, axis='Y', mat=HULL)
    # Hinge bosses — the jaws turn on these, so they read as load-bearing.
    for sz in (-1, 1):
        for sx in (-1, 1):
            p.cyl((sx * hw * 0.80, HINGE_Y, sz * HINGE_Z), 0.30, 0.28, 'X', 14,
                  STEEL)
            p.cyl((sx * (hw * 0.80 + 0.15), HINGE_Y, sz * HINGE_Z), 0.12, 0.16,
                  'X', 10, CHROME)
        p.cyl((0, HINGE_Y, sz * HINGE_Z), 0.13, hw * 1.75, 'X', 12, DARK)
        # Ram driving that jaw, anchored back down the palm.
        d = Vector((0.0, HINGE_Y - 0.34 - 1.30, sz * (HINGE_Z + 0.34) - sz * 0.10))
        rot = Matrix.Rotation(math.atan2(d.z, d.y), 4, 'X')
        p.cyl((0, 1.42, sz * 0.62), 0.19, 0.80, 'Y', 12, DARK, rot=rot)
        p.cyl((0, 1.72, sz * 0.86), 0.10, 1.00, 'Y', 10, CHROME, rot=rot)
    # Belly plate and a stencilled hazard chevron.
    p.box((0, 1.90, -1.16), (hw * 1.30, 0.80, 0.10), OLIVE)
    for k in range(3):
        p.box((-hw * 0.5 + k * hw * 0.5, 1.62, 1.18), (0.22, 0.34, 0.08), RED)
    p.cyl((0, 2.30, 1.12), 0.13, 0.16, 'Z', 10, BLACK)
    p.cyl((0, 2.30, 1.20), 0.09, 0.10, 'Z', 10, AMBER)


# ---------------------------------------------------------------------------
# Heavy — two opposed crusher pincers
# ---------------------------------------------------------------------------

HEAVY_CURVE = [(0.00, 0.00, 0.46, 0.44), (0.55, -0.05, 0.44, 0.42),
               (1.15, -0.18, 0.40, 0.38), (1.75, -0.39, 0.34, 0.32),
               (2.30, -0.66, 0.26, 0.25), (2.78, -0.95, 0.14, 0.16)]


def heavy_jaw(coll, name, sign):
    """One pincer. Origin on its hinge pin; curves toward the centreline."""
    p = Part(PALETTE)
    stations = [(y, sign * dz, hw, hh) for y, dz, hw, hh in HEAVY_CURVE]
    sweep(p, stations, HULL)
    # Heel behind the pivot — where the ram pushes, and what stops it closing
    # past the stop block.
    p.box((0, -0.34, sign * 0.30), (0.78, 0.62, 0.52), OLIVE)
    p.cyl((0, -0.42, sign * 0.44), 0.13, 0.86, 'X', 10, STEEL)
    p.cyl((0, 0, 0), 0.34, 0.92, 'X', 14, STEEL)
    p.cyl((0, 0, 0), 0.15, 1.06, 'X', 12, CHROME)
    # Interlocking teeth on the inner edge, offset so the two jaws mesh.
    for k in range(5):
        t = 0.30 + k * 0.15
        i = int(t * (len(HEAVY_CURVE) - 1))
        y, dz, hw, hh = HEAVY_CURVE[min(i, len(HEAVY_CURVE) - 1)]
        p.box((sign * (0.16 if k % 2 else -0.16), y + 0.18,
               sign * (dz - (hh - 0.02))),
              (0.26, 0.34, 0.30), STEEL)
    # Wear plate and rivets along the outer back of the pincer.
    p.rivets((0.0, 0.35, sign * 0.42), (0.0, 2.45, sign * -0.52), 7, 0.045,
             0.030, 'Z', STEEL)
    p.seam((0.30, 0.30, sign * 0.44), (0.16, 2.50, sign * -0.60), width=0.07,
           depth=0.05, axis='X', mat=RUST)
    p.bevel(width=0.016, segments=2)
    return p.finish(name, coll)


def variant_heavy(coll):
    p = Part(PALETTE)
    build_wrist(p)
    build_palm(p)
    # Stop blocks the jaw heels close against.
    for sz in (-1, 1):
        p.box((0, HINGE_Y - 0.52, sz * (HINGE_Z + 0.46)), (1.30, 0.26, 0.22),
              RUST)
    p.bevel(width=0.015, segments=2)
    p.finish("Mesh_Chela_Heavy_Palm", coll)
    heavy_jaw(coll, "Mesh_Chela_Heavy_JawUpper", 1).location = (
        0, HINGE_Y, HINGE_Z)
    heavy_jaw(coll, "Mesh_Chela_Heavy_JawLower", -1).location = (
        0, HINGE_Y, -HINGE_Z)


# ---------------------------------------------------------------------------
# Fine — precision picker
# ---------------------------------------------------------------------------

FINE_CURVE = [(0.00, 0.00, 0.20, 0.22), (0.70, -0.07, 0.18, 0.20),
              (1.45, -0.26, 0.15, 0.17), (2.15, -0.55, 0.11, 0.13),
              (2.70, -0.88, 0.06, 0.08)]


def fine_finger(coll, name, sign, x_off):
    p = Part(PALETTE)
    stations = [(y, sign * dz, hw, hh) for y, dz, hw, hh in FINE_CURVE]
    sweep(p, stations, STEEL, corner=0.14)
    p.cyl((0, 0, 0), 0.22, 0.40, 'X', 12, DARK)
    p.box((0, -0.26, sign * 0.20), (0.34, 0.44, 0.32), OLIVE)
    # Grip pads down the inside face.
    for k in range(4):
        y, dz, hw, hh = FINE_CURVE[min(k + 1, len(FINE_CURVE) - 1)]
        p.box((0, y, sign * (dz - hh)), (0.24, 0.26, 0.06), RUBBER)
    p.bevel(width=0.010, segments=2)
    obj = p.finish(name, coll)
    obj.location = (x_off, HINGE_Y, sign * HINGE_Z * 0.72)
    return obj


def variant_fine(coll):
    p = Part(PALETTE)
    build_wrist(p)
    build_palm(p, hw=0.80)
    # Sensor head sunk into the palm between the fingers — this is the variation
    # that looks at what it is picking up.
    p.box((0, 2.10, 0.0), (0.62, 0.46, 0.50), BLACK)
    p.cyl((0, 2.34, 0.0), 0.19, 0.14, 'Y', 14, GLASS)
    p.cyl((0, 2.40, 0.0), 0.11, 0.06, 'Y', 12, CRT)
    for sx in (-1, 1):
        p.cyl((sx * 0.34, 2.32, 0.22), 0.06, 0.10, 'Y', 8, AMBER)
    p.bevel(width=0.013, segments=2)
    p.finish("Mesh_Chela_Fine_Palm", coll)
    fine_finger(coll, "Mesh_Chela_Fine_FingerP", 1, 0.34)
    fine_finger(coll, "Mesh_Chela_Fine_FingerN", 1, -0.34)
    fine_finger(coll, "Mesh_Chela_Fine_Thumb", -1, 0.0)


# ---------------------------------------------------------------------------
# Cutter — anvil and shear
# ---------------------------------------------------------------------------

def variant_cutter(coll):
    p = Part(PALETTE)
    build_wrist(p)
    build_palm(p, hw=0.95)
    # Fixed anvil: part of the palm, because it never moves.
    sweep(p, [(HINGE_Y - 0.10, -HINGE_Z, 0.80, 0.34),
              (HINGE_Y + 0.90, -HINGE_Z - 0.10, 0.74, 0.30),
              (HINGE_Y + 1.90, -HINGE_Z - 0.26, 0.62, 0.26),
              (HINGE_Y + 2.60, -HINGE_Z - 0.42, 0.46, 0.22)], OLIVE, corner=0.14)
    for k in range(5):
        p.box((0, HINGE_Y + 0.30 + k * 0.50, -HINGE_Z - 0.06 - k * 0.07),
              (1.10, 0.16, 0.16), STEEL)
    # Cutting torch on the flank, with its bottle.
    p.cyl((0.92, 2.10, -0.30), 0.17, 0.90, 'Y', 12, RUST)
    p.cyl((0.92, 2.70, -0.30), 0.06, 0.44, 'Y', 8, CHROME)
    p.cyl((0.92, 2.95, -0.30), 0.05, 0.12, 'Y', 8, AMBER)
    p.cyl((-0.92, 1.80, -0.34), 0.22, 0.86, 'Y', 12, RED)
    p.bevel(width=0.014, segments=2)
    p.finish("Mesh_Chela_Cutter_Palm", coll)

    # The moving shear blade.
    q = Part(PALETTE)
    sweep(q, [(0.00, 0.00, 0.62, 0.40), (0.90, -0.22, 0.56, 0.34),
              (1.85, -0.60, 0.46, 0.28), (2.65, -1.05, 0.30, 0.20)],
          HULL, corner=0.16)
    # The edge itself — a chrome wedge along the underside.
    q.prism([(0.0, 0.0), (0.10, -0.26), (-0.10, -0.26)], 2.50, 'Y', CHROME,
            offset=(0, 1.30, -0.52))
    q.box((0, -0.36, 0.30), (0.92, 0.66, 0.56), OLIVE)
    q.cyl((0, 0, 0), 0.32, 1.02, 'X', 14, STEEL)
    q.cyl((0, 0, 0), 0.14, 1.16, 'X', 12, CHROME)
    q.cyl((0, -0.46, 0.46), 0.13, 0.94, 'X', 10, STEEL)
    q.rivets((0.0, 0.30, 0.34), (0.0, 2.30, -0.62), 6, 0.042, 0.028, 'Z', STEEL)
    q.bevel(width=0.014, segments=2)
    q.finish("Mesh_Chela_Cutter_Blade", coll).location = (0, HINGE_Y, HINGE_Z)


# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    variant_heavy(collection("Coll_Chela_Heavy"))
    variant_fine(collection("Coll_Chela_Fine"))
    variant_cutter(collection("Coll_Chela_Cutter"))

    print("\nClaw variations:")
    report()
    save(out)


build()
