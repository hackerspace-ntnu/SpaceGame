"""components/mechanical/engine_block — the land-ship's engine-room machinery.

Four pieces of heavy plant for the crawler's engine room: the main V8 diesel,
a tall inline six, a generator set and a transfer gearbox. They are sized for a
ship crewed by 3 m people, so the V8 comes up to a crewman's chest and the
gearbox sits at knee height.

Each engine is cut into the pieces a mechanic would unbolt — block, heads,
rocker covers, manifolds, flywheel housing, sump, mounts — and every piece is
its own object, parented to the block. That keeps a head liftable for a repair
scene, and lets an assembler swap a manifold for a pipe run without surgery.

Conventions: origin at the bottom centre where the engine sits on its bed,
crankshaft along Y, the front pulley at -Y and the flywheel / output at +Y.
Child pieces carry their own origins at their mating face or connection point.

    blender --background --python engine_block.py -- --out engine_block.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Metal_Steel_Dark",        # 0 DARK   heads, fittings, shafts, bolts
    "Mat_Metal_Steel_Worn",        # 1 STEEL  housings, feet, skid frame
    "Mat_Metal_HullRust_Orange",   # 2 HULL   the painted block, gone to rust
    "Mat_Metal_Rust_Heavy",        # 3 RUST   exhaust pipework, weld patches
    "Mat_Metal_Rust_Deep",         # 4 DEEP   sumps and undersides
    "Mat_Metal_Rust_Pale",         # 5 PALE   sun-flaked covers
    "Mat_Metal_Brass_Tarnished",   # 6 BRASS  filler caps, gauge bezels
    "Mat_Metal_Copper_Oxide",      # 7 COPPER dynamo windings, fuel lines
    "Mat_Plastic_Rubber_Black",    # 8 RUBBER cables, belts, hoses
    "Mat_Neutral_Black_Matte",     # 9 BLACK  pipe bores, grille backing
    "Mat_Emissive_Amber",          # 10 AMBER gauge faces
]
DARK, STEEL, HULL, RUST, DEEP, PALE, BRASS, COPPER, RUBBER, BLACK, AMBER = range(11)

BEVEL = 0.02


def along(a, b):
    d = Vector(b) - Vector(a)
    return Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4(), d.length


def pipe(p, a, b, radius, mat, seg=8, cap=True):
    """A straight pipe run between two points."""
    rot, length = along(a, b)
    return p.cyl((Vector(a) + Vector(b)) / 2.0, radius, length, 'Z', seg=seg,
                  mat=mat, rot=rot, cap=cap)


def attach(children, parent):
    """Parent rigidly without moving anything: pieces stay where they were built."""
    bpy.context.view_layer.update()
    inv = parent.matrix_world.inverted()
    for c in children:
        c.parent = parent
        c.matrix_parent_inverse = inv


def bolt_ring(p, center, radius, count, axis, bolt_r=0.028, depth=0.05,
              mat=DARK, phase=0.0):
    """Hex-ish bolt heads round a flange, standing proud along `axis`."""
    c = Vector(center)
    for k in range(count):
        a = 2 * math.pi * k / count + phase
        u, v = radius * math.cos(a), radius * math.sin(a)
        off = {'X': Vector((0, u, v)), 'Y': Vector((u, 0, v)),
               'Z': Vector((u, v, 0))}[axis]
        p.cyl(c + off, bolt_r, depth, axis, seg=6, mat=mat)


def mount_feet(p, xs, ys, height, mat=STEEL):
    """Bolted mounting feet: a pad on the bed and an upright gusset to the block."""
    for x in xs:
        for y in ys:
            p.box((x, y, 0.035), (0.26, 0.34, 0.07), mat)
            p.box((x - math.copysign(0.05, x), y, height / 2.0),
                  (0.10, 0.22, height), mat)


# ---------------------------------------------------------------------------
# Engine_DieselV8
# ---------------------------------------------------------------------------

V8_BANK = math.radians(30)          # each bank leans 30 deg off vertical
V8_DECK_X, V8_DECK_Z = 0.34, 1.20   # centre of each bank's head deck
V8_Y0 = -0.05                       # centre of the cylinder rows along Y
V8_PITCH = 0.62                     # cylinder spacing
V8_HEAD = (0.50, 0.56, 0.26)        # head size in the bank's own frame
V8_CRANK_Z = 0.70


def v8_bank(side):
    """Rotation and deck centre of one bank; side -1 is the -X bank."""
    rot = Matrix.Rotation(side * V8_BANK, 4, 'Y')
    return rot, Vector((side * V8_DECK_X, V8_Y0, V8_DECK_Z))


def v8_at(side, local):
    rot, deck = v8_bank(side)
    return deck + rot.to_3x3() @ Vector(local)


def v8_box(p, side, local, size, mat):
    rot, _ = v8_bank(side)
    return p.box(v8_at(side, local), size, mat, rot=rot)


def v8_block(coll, mats):
    p = Part(mats)
    # Crankcase and the V of the two banks, as one extruded section so the deck
    # faces land exactly under the heads.
    hw = V8_HEAD[0] / 2.0
    ro, di = v8_at(1, (hw, 0, 0)), v8_at(1, (-hw, 0, 0))
    lo, li = v8_at(-1, (-hw, 0, 0)), v8_at(-1, (hw, 0, 0))
    profile = [(-0.60, 0.30), (0.60, 0.30), (0.60, 1.00), (ro.x, ro.z - 0.01),
               (di.x, di.z - 0.01), (0.0, 1.16), (li.x, li.z - 0.01),
               (lo.x, lo.z - 0.01), (-0.60, 1.00)]
    body = p.prism(profile, 2.60, 'Y', HULL, offset=(0, V8_Y0, 0))
    # Timing cover and harmonic damper at the front.
    cover = p.box((0, -1.40, 0.82), (1.02, 0.14, 0.86), STEEL)
    p.cyl((0, -1.52, V8_CRANK_Z), 0.26, 0.12, 'Y', seg=12, mat=DARK)
    p.cyl((0, -1.555, V8_CRANK_Z), 0.09, 0.06, 'Y', seg=8, mat=BRASS)
    # Weld-on repair patch on the flank, and a stiffening rib under the deck.
    p.box((0.602, 0.40, 0.66), (0.012, 0.70, 0.42), RUST)
    p.rivets((0.61, 0.10, 0.84), (0.61, 0.70, 0.84), 3, axis='X', mat=DARK)
    p.rivets((0.61, 0.10, 0.48), (0.61, 0.70, 0.48), 3, axis='X', mat=DARK)
    for s in (-1, 1):
        p.box((s * 0.60, V8_Y0, 0.98), (0.06, 2.50, 0.06), STEEL)
    # Oil filler and dipstick in the valley.
    p.cyl((0, 0.95, 1.20), 0.07, 0.14, 'Z', seg=8, mat=BRASS)
    pipe(p, (-0.52, -0.9, 0.95), (-0.56, -0.9, 1.30), 0.015, BRASS, seg=6)
    p.bevel(cover, width=BEVEL, segments=1)
    p.shade(body, False)
    return p.finish("Mesh_Engine_DieselV8_Block", coll)


def v8_heads(side, name, coll, mats):
    p = Part(mats)
    heads = []
    stagger = 0.06 * side               # the two banks are offset by a rod width
    for i in range(4):
        y = (i - 1.5) * V8_PITCH + stagger
        heads += v8_box(p, side, (0, y, V8_HEAD[2] / 2.0 - 0.01), V8_HEAD, DARK)
    p.bevel(heads, width=BEVEL, segments=1)
    return p.finish(name, coll, origin=v8_at(side, (0, 0, 0)))


def v8_rocker(side, name, coll, mats):
    p = Part(mats)
    rot, _ = v8_bank(side)
    base = V8_HEAD[2] - 0.02
    shell = v8_box(p, side, (0, 0, base + 0.08), (0.36, 2.42, 0.17), PALE)
    for y in (-0.62, 0.62):
        p.cyl(v8_at(side, (0, y, base + 0.18)), 0.035, 0.05, 'Z', seg=6,
              mat=DARK, rot=rot)
    if side > 0:
        p.cyl(v8_at(side, (0, -0.95, base + 0.20)), 0.06, 0.08, 'Z', seg=8,
              mat=BRASS, rot=rot)
    p.bevel(shell, width=0.03, segments=1)
    return p.finish(name, coll, origin=v8_at(side, (0, 0, base)))


def v8_manifold(side, name, coll, mats):
    """Four short runners into a collector, rising to a flange for the stack."""
    p = Part(mats)
    cx, cz = side * 0.70, 0.98
    for i in range(4):
        y = (i - 1.5) * V8_PITCH + 0.06 * side
        port = v8_at(side, (side * (V8_HEAD[0] / 2.0 - 0.02), y, 0.12))
        pipe(p, port, (cx, y, cz), 0.055, RUST, seg=8, cap=False)
        p.box(port + Vector((side * 0.01, 0, 0)), (0.05, 0.16, 0.16), DARK)
    pipe(p, (cx, -1.18, cz), (cx, 1.14, cz), 0.075, RUST, seg=8)
    pipe(p, (cx, 1.07, cz + 0.05), (cx, 1.07, 1.68), 0.075, RUST, seg=8, cap=False)
    top = Vector((cx, 1.07, 1.70))
    p.cyl(top, 0.11, 0.06, 'Z', seg=8, mat=DARK)
    p.cyl(top + Vector((0, 0, 0.031)), 0.06, 0.004, 'Z', seg=8, mat=BLACK)
    return p.finish(name, coll, origin=top + Vector((0, 0, 0.03)))


def v8_flywheel(coll, mats):
    p = Part(mats)
    y0 = 1.25
    p.cyl((0, y0 + 0.03, V8_CRANK_Z), 0.70, 0.06, 'Y', seg=14, mat=STEEL)
    bell = p.cyl((0, y0 + 0.18, V8_CRANK_Z), 0.66, 0.24, 'Y', seg=14, mat=STEEL,
                 radius_top=0.50)
    bolt_ring(p, (0, y0 + 0.075, V8_CRANK_Z), 0.63, 4, 'Y', phase=0.4)
    p.cyl((0, y0 + 0.33, V8_CRANK_Z), 0.26, 0.08, 'Y', seg=12, mat=DARK)
    p.cyl((0, y0 + 0.395, V8_CRANK_Z), 0.18, 0.05, 'Y', seg=10, mat=DARK)
    p.box((0.46, y0 + 0.16, 0.40), (0.16, 0.14, 0.18), RUST)  # starter snout
    p.shade(bell)
    return p.finish("Mesh_Engine_DieselV8_FlywheelHousing", coll,
                    origin=(0, y0, V8_CRANK_Z))


def v8_sump(coll, mats):
    p = Part(mats)
    pan = p.prism([(-0.50, 0.31), (-0.44, 0.10), (0.44, 0.10), (0.50, 0.31)],
                  2.20, 'Y', DEEP, offset=(0, -0.15, 0))
    p.box((0, -0.15, 0.315), (1.06, 2.26, 0.03), DARK)          # pan flange
    p.cyl((0.20, 0.70, 0.08), 0.05, 0.05, 'Z', seg=6, mat=BRASS)  # drain plug
    p.bevel(pan, width=0.03, segments=1)
    return p.finish("Mesh_Engine_DieselV8_Sump", coll, origin=(0, -0.15, 0.30))


def v8_mounts(coll, mats):
    p = Part(mats)
    mount_feet(p, (-0.66, 0.66), (-0.95, 0.70), 0.62)
    return p.finish("Mesh_Engine_DieselV8_Mounts", coll)


def build_v8(coll, mats):
    block = v8_block(coll, mats)
    parts = [v8_mounts(coll, mats), v8_sump(coll, mats), v8_flywheel(coll, mats)]
    for side, tag in ((-1, "L"), (1, "R")):
        parts.append(v8_heads(side, "Mesh_Engine_DieselV8_Heads_" + tag, coll, mats))
        parts.append(v8_rocker(side, "Mesh_Engine_DieselV8_RockerCover_" + tag, coll, mats))
        parts.append(v8_manifold(side, "Mesh_Engine_DieselV8_Manifold_" + tag, coll, mats))
    attach(parts, block)


# ---------------------------------------------------------------------------
# Engine_Inline6
# ---------------------------------------------------------------------------

I6_PITCH = 0.40
I6_CRANK_Z = 0.62


def i6_block(coll, mats):
    p = Part(mats)
    crank = p.slab((-0.45, -1.30, 0.28), (0.45, 1.20, 1.10), HULL)
    upper = p.slab((-0.36, -1.28, 1.09), (0.36, 1.18, 1.52), HULL)
    p.box((0, -1.36, 0.95), (0.70, 0.12, 0.90), STEEL)           # timing case
    for i in range(3):                                           # crankcase doors
        y = -0.80 + i * 0.80
        p.box((0.455, y, 0.72), (0.012, 0.46, 0.36), PALE)
        p.cyl((0.466, y, 0.72), 0.05, 0.02, 'X', seg=6, mat=DARK)
    p.box((-0.455, 0.35, 0.60), (0.012, 0.8, 0.30), RUST)        # weld patch
    p.rivets((-0.463, -0.02, 0.73), (-0.463, 0.72, 0.73), 3, axis='X', mat=DARK)
    p.bevel(crank + upper, width=BEVEL, segments=1)
    return p.finish("Mesh_Engine_Inline6_Block", coll)


def i6_heads(coll, mats):
    p = Part(mats)
    for i in range(6):
        y = (i - 2.5) * I6_PITCH - 0.05
        p.box((0, y, 1.64), (0.66, 0.37, 0.26), DARK)
    return p.finish("Mesh_Engine_Inline6_Heads", coll, origin=(0, -0.05, 1.51))


def i6_rocker(coll, mats):
    p = Part(mats)
    shell = p.box((0, -0.05, 1.855), (0.50, 2.36, 0.19), PALE)
    for i in (0, 2, 4):
        p.cyl((0, (i - 2.5) * I6_PITCH + 0.15, 1.965), 0.035, 0.04, 'Z', seg=6,
              mat=DARK)
    p.cyl((0.12, 0.95, 1.975), 0.055, 0.06, 'Z', seg=8, mat=BRASS)
    p.bevel(shell, width=0.03, segments=1)
    return p.finish("Mesh_Engine_Inline6_RockerCover", coll, origin=(0, -0.05, 1.77))


def i6_intake(coll, mats):
    """The side intake: a plenum pipe along -X fed by an air-cleaner drum."""
    p = Part(mats)
    x, z = -0.44, 1.40
    pipe(p, (x, -1.12, z), (x, 1.26, z), 0.075, STEEL, seg=8)
    for i in range(6):
        y = (i - 2.5) * I6_PITCH - 0.05
        pipe(p, (x + 0.03, y, z + 0.03), (-0.30, y, 1.62), 0.045, STEEL, seg=6,
             cap=False)
    p.cyl((x, 1.32, z), 0.09, 0.08, 'Y', seg=8, mat=DARK)
    pipe(p, (x, 1.32, z), (x, 1.32, 1.70), 0.075, RUBBER, seg=8, cap=False)
    p.cyl((-0.36, 1.32, 1.82), 0.18, 0.28, 'Z', seg=12, mat=HULL)    # air cleaner
    p.cyl((-0.36, 1.32, 1.975), 0.19, 0.03, 'Z', seg=12, mat=DARK)
    return p.finish("Mesh_Engine_Inline6_Intake", coll, origin=(x, 1.32, z))


def i6_exhaust(coll, mats):
    p = Part(mats)
    x, z = 0.44, 1.34
    for i in range(6):
        y = (i - 2.5) * I6_PITCH - 0.05
        pipe(p, (0.30, y, 1.60), (x, y, z), 0.05, RUST, seg=8, cap=False)
    pipe(p, (x, -1.12, z), (x, 0.95, z), 0.07, RUST, seg=8)
    pipe(p, (x, 0.90, z + 0.05), (x, 0.90, 1.90), 0.07, RUST, seg=8, cap=False)
    top = Vector((x, 0.90, 1.92))
    p.cyl(top, 0.10, 0.05, 'Z', seg=8, mat=DARK)
    p.cyl(top + Vector((0, 0, 0.026)), 0.055, 0.004, 'Z', seg=8, mat=BLACK)
    return p.finish("Mesh_Engine_Inline6_Exhaust", coll, origin=top + Vector((0, 0, 0.025)))


def i6_pulley(coll, mats):
    """The big front pulley, its belt, and the fan-drive idler it turns."""
    p = Part(mats)
    y = -1.47
    p.cyl((0, y, I6_CRANK_Z), 0.42, 0.10, 'Y', seg=18, mat=DARK)
    p.cyl((0, y - 0.07, I6_CRANK_Z), 0.12, 0.05, 'Y', seg=8, mat=BRASS)
    p.cyl((0.18, y, 1.30), 0.16, 0.10, 'Y', seg=10, mat=DARK)     # idler
    # Belt: two straight runs tangent-ish to both pulleys.
    for s in (-1, 1):
        pipe(p, (s * 0.41 + 0.0, y, I6_CRANK_Z + 0.05),
             (0.18 + s * 0.155, y, 1.28), 0.03, RUBBER, seg=6, cap=False)
    return p.finish("Mesh_Engine_Inline6_FrontPulley", coll, origin=(0, -1.42, I6_CRANK_Z))


def i6_flywheel(coll, mats):
    p = Part(mats)
    y0 = 1.20
    p.cyl((0, y0 + 0.03, I6_CRANK_Z), 0.52, 0.06, 'Y', seg=12, mat=STEEL)
    p.cyl((0, y0 + 0.15, I6_CRANK_Z), 0.49, 0.18, 'Y', seg=12, mat=STEEL,
          radius_top=0.38)
    bolt_ring(p, (0, y0 + 0.075, I6_CRANK_Z), 0.47, 4, 'Y', phase=0.4)
    p.cyl((0, y0 + 0.27, I6_CRANK_Z), 0.16, 0.06, 'Y', seg=8, mat=DARK)
    return p.finish("Mesh_Engine_Inline6_FlywheelHousing", coll, origin=(0, y0, I6_CRANK_Z))


def i6_sump(coll, mats):
    p = Part(mats)
    pan = p.prism([(-0.40, 0.29), (-0.34, 0.08), (0.34, 0.08), (0.40, 0.29)],
                  2.10, 'Y', DEEP, offset=(0, -0.05, 0))
    p.cyl((-0.15, 0.6, 0.06), 0.04, 0.04, 'Z', seg=6, mat=BRASS)
    p.bevel(pan, width=0.025, segments=1)
    return p.finish("Mesh_Engine_Inline6_Sump", coll, origin=(0, -0.05, 0.28))


def i6_mounts(coll, mats):
    p = Part(mats)
    mount_feet(p, (-0.52, 0.52), (-0.85, 0.75), 0.56)
    return p.finish("Mesh_Engine_Inline6_Mounts", coll)


def build_i6(coll, mats):
    block = i6_block(coll, mats)
    attach([i6_heads(coll, mats), i6_rocker(coll, mats), i6_intake(coll, mats),
            i6_exhaust(coll, mats), i6_pulley(coll, mats),
            i6_flywheel(coll, mats), i6_sump(coll, mats),
            i6_mounts(coll, mats)], block)


# ---------------------------------------------------------------------------
# Engine_Generator
# ---------------------------------------------------------------------------

GEN_SHAFT_Z = 0.72


def gen_skid(coll, mats):
    p = Part(mats)
    rails = []
    for s in (-1, 1):
        rails += p.slab((s * 0.52 - 0.08, -1.10, 0.0), (s * 0.52 + 0.08, 1.10, 0.18), STEEL)
    for y in (-0.95, -0.15, 0.95):
        p.slab((-0.44, y - 0.06, 0.02), (0.44, y + 0.06, 0.16), DARK)
    p.bevel(rails, width=0.015, segments=1)
    return p.finish("Mesh_Engine_Generator_Skid", coll)


def gen_engine(coll, mats):
    """A small two-cylinder donkey engine with a radiator at the -Y end."""
    p = Part(mats)
    body = p.slab((-0.34, -0.80, 0.18), (0.34, -0.10, 0.98), HULL)
    head = p.slab((-0.28, -0.74, 0.97), (0.28, -0.16, 1.16), DARK)
    p.box((0, -0.45, 1.20), (0.34, 0.50, 0.09), PALE)            # rocker cover
    # Radiator block with a slatted face.
    p.slab((-0.42, -1.08, 0.18), (0.42, -0.84, 1.30), STEEL)
    p.slab((-0.36, -1.085, 0.30), (0.36, -1.075, 1.20), BLACK)
    for i in range(6):
        p.box((0, -1.09, 0.36 + i * 0.16), (0.74, 0.02, 0.05), DARK)
    p.cyl((0.20, -0.96, 1.36), 0.05, 0.10, 'Z', seg=6, mat=BRASS)  # filler cap
    pipe(p, (0, -0.84, 1.00), (0, -0.74, 1.05), 0.05, RUBBER, seg=6, cap=False)
    # Exhaust stub and its rain flap.
    pipe(p, (0.34, -0.50, 0.80), (0.46, -0.50, 0.80), 0.05, RUST, seg=8, cap=False)
    pipe(p, (0.46, -0.50, 0.76), (0.46, -0.50, 1.45), 0.05, RUST, seg=8)
    p.box((0.46, -0.50, 1.48), (0.13, 0.12, 0.012), DARK, rot=Matrix.Rotation(math.radians(20), 4, 'X'))
    p.bevel(body + head, width=BEVEL, segments=1)
    return p.finish("Mesh_Engine_Generator_Engine", coll, origin=(0, -0.45, 0.18))


def gen_dynamo(coll, mats):
    """The drum dynamo, its cradle, and the coupling from the engine."""
    p = Part(mats)
    y0, y1 = 0.08, 1.02
    ym = (y0 + y1) / 2.0
    p.cyl((0, ym, GEN_SHAFT_Z), 0.52, y1 - y0 - 0.16, 'Y', seg=14, mat=HULL)
    for y, r1, r2 in ((y0 + 0.04, 0.40, 0.52), (y1 - 0.04, 0.52, 0.40)):
        p.cyl((0, y, GEN_SHAFT_Z), r1, 0.08, 'Y', seg=14, mat=STEEL, radius_top=r2)
    p.cyl((0, ym + 0.12, GEN_SHAFT_Z), 0.535, 0.08, 'Y', seg=14, mat=DARK)  # clamp band
    p.slab((-0.25, ym - 0.30, GEN_SHAFT_Z - 0.53), (0.25, ym + 0.30, 0.10), COPPER)
    for s in (-1, 1):                                            # cradle feet
        p.slab((s * 0.30 - 0.08, ym - 0.32, 0.16), (s * 0.30 + 0.08, ym + 0.32, 0.36), STEEL)
    p.cyl((0, -0.02, GEN_SHAFT_Z), 0.07, 0.20, 'Y', seg=8, mat=DARK)   # shaft
    p.cyl((0, -0.03, GEN_SHAFT_Z), 0.16, 0.06, 'Y', seg=8, mat=BRASS)  # coupling
    p.cyl((0, y1 + 0.03, GEN_SHAFT_Z), 0.12, 0.06, 'Y', seg=8, mat=DARK)
    return p.finish("Mesh_Engine_Generator_Dynamo", coll, origin=(0, y0, GEN_SHAFT_Z))


def gen_panel(coll, mats):
    """Gauge panel on a bracket off the dynamo's -X flank, facing -X."""
    p = Part(mats)
    x = -0.60
    box = p.slab((x, 0.28, 0.82), (x + 0.14, 0.92, 1.30), STEEL)
    p.slab((x + 0.13, 0.50, 0.90), (-0.43, 0.70, 1.00), DARK)   # bracket
    for y, z, r in ((0.43, 1.14, 0.10), (0.68, 1.14, 0.10), (0.80, 0.94, 0.06)):
        p.cyl((x - 0.012, y, z), r + 0.02, 0.024, 'X', seg=8, mat=BRASS)
        p.cyl((x - 0.026, y, z), r, 0.006, 'X', seg=8, mat=AMBER)
    p.box((x - 0.02, 0.45, 0.93), (0.04, 0.05, 0.10), DARK)      # breaker lever
    p.box((x - 0.012, 0.60, 1.27), (0.024, 0.52, 0.03), RUST)    # drip streak
    p.bevel(box, width=0.012, segments=1)
    return p.finish("Mesh_Engine_Generator_GaugePanel", coll, origin=(x + 0.14, 0.60, 0.95))


def gen_cable(coll, mats):
    """Terminal box on top of the dynamo and the cable dropping off the +X side."""
    p = Part(mats)
    top = GEN_SHAFT_Z + 0.52
    term = p.slab((-0.14, 0.40, top - 0.03), (0.14, 0.72, top + 0.14), DARK)
    p.cyl((0.20, 0.56, top + 0.06), 0.06, 0.12, 'X', seg=8, mat=BRASS)   # gland
    pts = [(0.26, 0.56, top + 0.06), (0.46, 0.56, top - 0.02),
           (0.60, 0.60, top - 0.30), (0.62, 0.70, 0.40), (0.62, 0.90, 0.20),
           (0.62, 1.12, 0.20)]
    for a, b in zip(pts, pts[1:]):
        pipe(p, a, b, 0.045, RUBBER, seg=6, cap=False)
    p.cyl((0.62, 1.10, 0.20), 0.065, 0.06, 'Y', seg=8, mat=DARK)  # plug
    p.bevel(term, width=0.012, segments=1)
    return p.finish("Mesh_Engine_Generator_CableOutlet", coll, origin=(0.62, 1.13, 0.20))


def build_generator(coll, mats):
    skid = gen_skid(coll, mats)
    attach([gen_engine(coll, mats), gen_dynamo(coll, mats), gen_panel(coll, mats),
            gen_cable(coll, mats)], skid)


# ---------------------------------------------------------------------------
# Engine_Gearbox
# ---------------------------------------------------------------------------

GB_SHAFT_Z = 0.46


def gb_case(coll, mats):
    p = Part(mats)
    case = p.slab((-0.36, -0.62, 0.10), (0.36, 0.62, 0.80), HULL)
    hump = p.cyl((0, 0.20, 0.80), 0.38, 0.70, 'X', seg=12, mat=HULL)  # gear hump
    for s in (-1, 1):                                                  # bearing bosses
        p.cyl((s * 0.38, 0, GB_SHAFT_Z), 0.22, 0.08, 'X', seg=12, mat=STEEL)
    for x in (-0.46, 0.46):                                            # feet
        p.slab((x - 0.12, -0.55, 0.0), (x + 0.12, 0.55, 0.10), STEEL)
    p.bevel(case, width=BEVEL, segments=1)
    p.shade(hump)
    return p.finish("Mesh_Engine_Gearbox_Case", coll)


def gb_cover(coll, mats):
    p = Part(mats)
    plate = p.slab((-0.31, -0.60, 0.795), (0.31, -0.20, 0.855), PALE)
    p.rivets((-0.30, -0.56, 0.855), (0.30, -0.56, 0.855), 4, radius=0.03, height=0.02, mat=DARK)
    p.rivets((-0.30, -0.24, 0.855), (0.30, -0.24, 0.855), 4, radius=0.03, height=0.02, mat=DARK)
    p.cyl((0.14, -0.40, 0.88), 0.06, 0.06, 'Z', seg=8, mat=BRASS)       # breather
    p.bevel(plate, width=0.012, segments=1)
    return p.finish("Mesh_Engine_Gearbox_Cover", coll, origin=(0, -0.40, 0.795))


def gb_shafts(coll, mats):
    """Output shafts to both sides along X, and the input stub on -Y."""
    p = Part(mats)
    p.cyl((0, 0, GB_SHAFT_Z), 0.07, 1.10, 'X', seg=8, mat=DARK)
    for s in (-1, 1):
        p.cyl((s * 0.57, 0, GB_SHAFT_Z), 0.17, 0.06, 'X', seg=8, mat=STEEL)
    p.cyl((0, -0.66, 0.34), 0.08, 0.10, 'Y', seg=8, mat=DARK)
    p.cyl((0, -0.68, 0.34), 0.16, 0.04, 'Y', seg=10, mat=STEEL)
    return p.finish("Mesh_Engine_Gearbox_Shafts", coll, origin=(0, 0, GB_SHAFT_Z))


def build_gearbox(coll, mats):
    case = gb_case(coll, mats)
    attach([gb_cover(coll, mats), gb_shafts(coll, mats)], case)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_v8(collection("Coll_Engine_DieselV8", root), mats)
    build_i6(collection("Coll_Engine_Inline6", root), mats)
    build_generator(collection("Coll_Engine_Generator", root), mats)
    build_gearbox(collection("Coll_Engine_Gearbox", root), mats)

    report()
    save(out)


if __name__ == "__main__":
    main()
