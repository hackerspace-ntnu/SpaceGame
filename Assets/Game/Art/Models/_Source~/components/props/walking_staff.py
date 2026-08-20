"""components/props/walking_staff — hand-cut wooden staves, canes and walking sticks.

Built for the Nomad, who carries one and swings it at anything that hits him
first, but a walking stick is the most reusable prop a desert settlement can
have: it is a hiker's third leg, a herder's goad, a market-stall awning pole and
a lean-to ridge in four different scenes.

Four variations, chosen so they differ in SILHOUETTE rather than in colour --
the failure mode for a family of sticks is four brown lines of slightly
different length, which reads as one stick and a scale bug:

  Coll_Staff_Nomad     1.62 m  straight-grown, burl below the grip, cord wrap,
                               iron ferrule, wrist thong. The hero.
  Coll_Staff_Gnarled   1.48 m  a raw branch -- hard kinks, forked crown, stubs
                               where side limbs were cut off. No metal at all.
  Coll_Staff_Cane      1.02 m  short cane with a T crossbar handle. Two thirds
                               the height and a completely different top.
  Coll_Staff_Splinted  1.55 m  snapped mid-shaft and repaired: a scrap-plate
                               splint over the break, whipped down with cord.

Everything is made from materials that already existed. The Wood category holds
exactly one entry (Mat_Wood_Ply_Worn) and it was tempting to add a pale
sun-bleached wood next to it, but the palette check put every candidate in that
band within deltaE 3 of Mat_Plastic_Cream_Aged -- so the shafts take the plywood
brown and the contrast comes from the cord, iron and leather instead. See
walking_staff_BUILD.md.

ORIGIN: the centre of the grip, NOT the butt of the shaft.

    A staff spends its life either planted on the ground or held in a fist, and
    only one of those can be the origin. The grip wins because that is the
    attachment this family was built for -- parenting to a hand bone is then a
    zero-offset parent, with no per-variation number to look up and get wrong.
    The butt therefore sits at negative Z, and a staff stood upright on the
    ground has to be raised by its own grip height. That is the trade.

No armature. Nothing on a stick moves; a break is modelled as a repair, not as
a joint. Adding one would be cost with no capability.

    blender --background --python walking_staff.py -- --out walking_staff.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

from mathutils import Matrix

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, LIB)
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Wood_Ply_Worn",         # 0  every shaft, crossbar, fork and stub
    "Mat_Fabric_Canvas_Faded",   # 1  cord grip wrap and splint whipping
    "Mat_Metal_Steel_Worn",      # 2  ferrules and collars still sound
    "Mat_Metal_Rust_Heavy",      # 3  the corroded ferrule and splint plate
    "Mat_Hide_Claw_Horn",        # 4  leather wrist thong
]
WOOD, CORD, STEEL, RUST, HIDE = range(5)

# Ten sides is plenty for something 35 mm thick that is never seen further from
# the camera than a character's hand, and it keeps a four-stave family under a
# couple of thousand triangles.
SEG = 10


def ring(radius, cx=0.0, cy=0.0, seg=SEG):
    """A circular loft profile of `seg` points, off-centred by (cx, cy).

    Off-centring is the whole trick behind every bend in this file: a shaft is
    one loft whose stations wander laterally, rather than a chain of separate
    cylinders that would leave a visible crease at each joint.
    """
    return [(cx + radius * math.cos(2 * math.pi * i / seg),
             cy + radius * math.sin(2 * math.pi * i / seg))
            for i in range(seg)]


def shaft(p, stations, mat=WOOD, seg=SEG):
    """Loft a shaft through `stations` — each (z, radius, x_offset, y_offset).

    A local radius bulge is how knots and burls are made: growing the station
    is one number and stays watertight, where a sphere stuck on the side would
    leave interior faces inside the shaft.
    """
    sections = [(z, ring(r, cx, cy, seg)) for z, r, cx, cy in stations]
    return p.loft(sections, axis='Z', mat=mat, cap=True)


def whipping(p, z, radius, mat=CORD, width=0.020, seg=SEG):
    """One band of cord wrapped round the shaft."""
    return p.cyl((0, 0, z), radius, width, axis='Z', seg=seg, mat=mat)


def stub(p, base, direction, length, radius, mat=WOOD):
    """A sawn-off side limb, angled away from the shaft.

    Points along `direction` from `base`; the cone taper is the saw cut.
    """
    d = _norm(direction)
    centre = (base[0] + d[0] * length * 0.5,
              base[1] + d[1] * length * 0.5,
              base[2] + d[2] * length * 0.5)
    return p.cyl(centre, radius, length, axis='Z', seg=6, mat=mat,
                 radius_top=radius * 0.62, rot=_aim(d))


def _norm(v):
    n = math.sqrt(sum(c * c for c in v)) or 1.0
    return (v[0] / n, v[1] / n, v[2] / n)


def _aim(d):
    """Rotation taking +Z onto unit vector `d`."""
    yaw = math.atan2(d[1], d[0])
    pitch = math.acos(max(-1.0, min(1.0, d[2])))
    return Matrix.Rotation(yaw, 4, 'Z') @ Matrix.Rotation(pitch, 4, 'Y')


# --------------------------------------------------------------------------
# 1 — Coll_Staff_Nomad : the hero stave
# --------------------------------------------------------------------------

def staff_nomad(mats, coll):
    p = Part(mats)

    # Grown straight but not machine-straight: the stations drift about 12 mm
    # off the axis and back. A perfectly straight shaft is the single strongest
    # tell that a stick was made in software.
    shaft(p, [
        (-1.320, 0.0165,  0.010,  0.000),
        (-1.100, 0.0182,  0.006,  0.002),
        (-0.850, 0.0196, -0.004,  0.004),
        (-0.600, 0.0206, -0.012,  0.003),
        (-0.380, 0.0224, -0.010,  0.000),
        (-0.310, 0.0246, -0.009, -0.001),
        (-0.265, 0.0362, -0.006, -0.003),   # burl, just below the hand
        (-0.222, 0.0258, -0.006, -0.002),
        (-0.180, 0.0232, -0.006, -0.002),
        ( 0.000, 0.0215,  0.000, -0.002),   # grip / origin
        ( 0.160, 0.0198,  0.004, -0.001),
        ( 0.262, 0.0175,  0.008,  0.000),
        ( 0.300, 0.0120,  0.009,  0.000),   # crown, rounded off
    ])

    # Iron ferrule: the reason the butt has not splintered after a thousand
    # kilometres of gravel. Tapered, and sunk 5 mm over the wood.
    p.cyl((0.010, 0, -1.297), 0.0212, 0.078, axis='Z', seg=SEG, mat=STEEL,
          radius_top=0.0176)

    # Cord grip: one sleeve with a binding turn at each end, so the wrap reads
    # as wound rather than as a rubber sock.
    p.cyl((0, 0, 0.020), 0.0248, 0.230, axis='Z', seg=SEG, mat=CORD)
    whipping(p, -0.092, 0.0268)
    whipping(p, 0.132, 0.0268)

    # Wrist thong through a hole below the crown.
    p.torus((0.007, 0, 0.208), 0.034, 0.0055, axis='Y', maj_seg=14, min_seg=6,
            mat=HIDE)

    return p.finish("Mesh_Staff_Nomad", coll)


# --------------------------------------------------------------------------
# 2 — Coll_Staff_Gnarled : a branch, barely worked
# --------------------------------------------------------------------------

def staff_gnarled(mats, coll):
    p = Part(mats)

    # Three hard kinks and a radius that swells and pinches along its length.
    # This is the variation that has to read as "picked up", so nothing about
    # it is regular.
    shaft(p, [
        (-1.220, 0.0210,  0.026,  0.008),
        (-1.040, 0.0186,  0.010, -0.004),
        (-0.880, 0.0262, -0.014, -0.012),   # knot
        (-0.700, 0.0192, -0.032, -0.006),
        (-0.520, 0.0205, -0.024,  0.010),
        (-0.330, 0.0288, -0.002,  0.018),   # knot
        (-0.170, 0.0206,  0.014,  0.010),
        ( 0.000, 0.0224,  0.020, -0.002),   # grip / origin
        ( 0.120, 0.0198,  0.012, -0.010),
        ( 0.185, 0.0182,  0.004, -0.012),
    ])

    # Forked crown — two limbs left on where the branch divided. This is the
    # silhouette difference that carries the variation at distance.
    p.cyl((0.004, -0.012, 0.185), 0.0150, 0.001, axis='Z', seg=6, mat=WOOD)
    stub(p, (0.004, -0.012, 0.184), (-0.34, -0.16, 1.0), 0.150, 0.0128)
    stub(p, (0.004, -0.012, 0.184), (0.42, 0.10, 1.0), 0.108, 0.0112)

    # Side limbs cut back to the shaft.
    stub(p, (-0.012, -0.010, -0.880), (-0.86, -0.34, 0.38), 0.062, 0.0092)
    stub(p, (-0.002, 0.016, -0.330), (0.28, 0.90, 0.33), 0.048, 0.0080)
    stub(p, (0.010, -0.002, -1.040), (0.62, -0.70, -0.35), 0.037, 0.0068)

    return p.finish("Mesh_Staff_Gnarled", coll)


# --------------------------------------------------------------------------
# 3 — Coll_Staff_Cane : short, with a T handle
# --------------------------------------------------------------------------

def staff_cane(mats, coll):
    p = Part(mats)

    # Two thirds the height of the staves and topped by a crossbar, so it
    # cannot be mistaken for any of them even in silhouette.
    shaft(p, [
        (-0.990, 0.0142,  0.004,  0.000),
        (-0.780, 0.0152,  0.002,  0.002),
        (-0.520, 0.0163, -0.004,  0.002),
        (-0.260, 0.0172, -0.005,  0.000),
        (-0.060, 0.0178, -0.002, -0.001),
        ( 0.000, 0.0180,  0.000, -0.001),   # under the collar / origin
        ( 0.022, 0.0176,  0.001, -0.001),
    ])

    # Collar hiding the joint between shaft and handle.
    p.cyl((0.001, 0, 0.026), 0.0206, 0.024, axis='Z', seg=SEG, mat=STEEL)

    # T crossbar, worn smooth by a hand rather than wrapped.
    p.cyl((0.001, 0, 0.049), 0.0176, 0.168, axis='X', seg=SEG, mat=WOOD)
    for side in (-1, 1):
        p.cyl((0.001 + side * 0.084, 0, 0.049), 0.0176, 0.010, axis='X',
              seg=SEG, mat=WOOD, radius_top=0.0132)

    # Steel ferrule, still sound.
    p.cyl((0.004, 0, -0.972), 0.0178, 0.062, axis='Z', seg=SEG, mat=STEEL,
          radius_top=0.0148)

    return p.finish("Mesh_Staff_Cane", coll)


# --------------------------------------------------------------------------
# 4 — Coll_Staff_Splinted : snapped and field-repaired
# --------------------------------------------------------------------------

def staff_splinted(mats, coll):
    p = Part(mats)

    BREAK = -0.550

    # The kink at the break is deliberate and is the whole point of the
    # variation: a repair that realigned perfectly would be invisible, and the
    # splint would read as decoration.
    shaft(p, [
        (-1.270, 0.0168,  0.030,  0.004),
        (-1.020, 0.0184,  0.024,  0.002),
        (-0.780, 0.0198,  0.018,  0.000),
        (BREAK,  0.0206,  0.010, -0.002),   # break face
        (-0.400, 0.0202, -0.004, -0.004),   # ... and the shaft above it, off-axis
        (-0.180, 0.0214, -0.010, -0.002),
        ( 0.000, 0.0220, -0.010,  0.000),   # grip / origin
        ( 0.160, 0.0202, -0.006,  0.002),
        ( 0.244, 0.0180, -0.002,  0.002),
        ( 0.280, 0.0124,  0.000,  0.002),
    ])

    # Two scrap plates bridging the break, on opposite faces. Bevelled here and
    # nowhere else in this file -- these are the only box-like pieces, and a
    # whole-part bevel would weld the thin lofted shafts into a blob.
    plate = []
    for side in (-1, 1):
        plate += p.slab((side * 0.019 - 0.006, -0.021, BREAK - 0.135),
                        (side * 0.031 - 0.006, 0.021, BREAK + 0.135), RUST)
    p.bevel(plate, width=0.004, segments=1)

    # Cord whipped over the plate ends and the middle.
    for z in (BREAK - 0.118, BREAK + 0.002, BREAK + 0.118):
        whipping(p, z, 0.0345, width=0.026)

    # Grip wrap, same construction as the hero staff.
    p.cyl((-0.008, 0, 0.010), 0.0252, 0.210, axis='Z', seg=SEG, mat=CORD)
    whipping(p, -0.088, 0.0272)
    whipping(p, 0.108, 0.0272)

    # Ferrule. Steel, not Mat_Metal_Rust_Heavy: that material is a saturated
    # orange meant to be read as a streak across a large hull panel, and on a
    # 20 mm end cap it stops looking like corrosion and starts looking like a
    # moulded plastic tip. The rust stays on the splint plates, which are big
    # enough to carry it.
    p.cyl((0.031, 0, -1.248), 0.0214, 0.070, axis='Z', seg=SEG, mat=STEEL,
          radius_top=0.0180)

    return p.finish("Mesh_Staff_Splinted", coll)


# --------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    staff_nomad(mats, collection("Coll_Staff_Nomad"))
    staff_gnarled(mats, collection("Coll_Staff_Gnarled"))
    staff_cane(mats, collection("Coll_Staff_Cane"))
    staff_splinted(mats, collection("Coll_Staff_Splinted"))

    report()
    save(out)


main()
