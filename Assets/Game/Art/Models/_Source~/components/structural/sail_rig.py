"""Tensile shade-sail rigging: timber masts and ground anchors.

Everything here is wood and rope. No metal, no fittings, no machined parts — a
nomad camp pitches its sails on cut poles lashed with hemp, and a steel mast
with a forged eye reads as somebody else's infrastructure standing in the sand.

The sails are pitched on raked masts, and a raked mast has to be a clean shaft —
the awning kit's poles (`Mesh_AwningStrip_Pole`, `Mesh_AwningPorch_PoleLeft`)
each carry an outrigger brace modelled for a vertical stance, and tipping one
lays its brace across the sail it is holding up. These are the poles that rake.

Four masts and three anchors, each its own collection:

  Mast_Pole       one tapered pole, hemp whipping at the head - the default
  Mast_Lashed     two poles spliced and lashed, for the heavy corners
  Mast_Tripod     three poles crossed and bound - shear legs that stand anywhere
  Mast_Stub       a short stout post for a corner pulled near the ground
  Anchor_Stake    a driven peg with a rope turn, 0.28 m - the default tie-down
  Anchor_Cleat    a buried deadman board pinned by two stakes
  Anchor_Log      a short log laid on the sand with rope round it

Masts are modelled along +Z with their foot at z = 0 and their axis on x = y = 0,
so a caller can scale one to a length and rake it about its foot without the
shaft walking off the point it was planted on. Anchors sit on z = 0 the same way.

The tie point is the hemp whipping at the head, not a ring: its height is what a
caller scales by, and the sail corner and its guy both land on it.

Poles are **thin**: 84 mm at the butt on a 3.6 m mast, tapering to 56 mm. Cut
timber that holds a fabric sail is a spar, not a pile — a pole thick enough to
read as structural makes the whole camp look like scaffolding. A caller that
scales a mast to length must not scale its girth by the same factor or the tall
masts fatten; see `GIRTH_POW` in `components/nomad_settlement/tents.py`.

Shafts are 8-sided on purpose. A 16-segment barrel reads as a machined tube; at
eight facets a pole reads as something cut and shaved by hand.

    blender --background --python sail_rig.py -- --out <path.blend>
"""

import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.abspath(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")))
import _buildlib as bl  # noqa: E402

MATS = ["Mat_Wood_Timber_Silvered", "Mat_Wood_Ply_Worn", "Mat_Fabric_Rope_Hemp"]
TIMBER, PLY, ROPE = 0, 1, 2

MAST_LEN = 3.60          # every full mast is modelled to this tie height
STUB_LEN = 1.40


def emit(coll, name, mats, build, bevel=0.005):
    p = bl.Part(mats)
    build(p)
    if bevel:
        p.bevel(width=bevel, segments=2)
    return p.finish(name, coll, origin=(0, 0, 0))


def whipping(p, z, shaft_r, minor=0.012):
    """The hemp turn round the pole that the sail corner and its guy tie onto.

    Sized off the shaft at that height so the rope bites into the wood rather
    than floating round it: the inner radius sits inside the pole.
    """
    p.torus((0, 0, z), shaft_r + minor - 0.004, minor, axis='Z',
            maj_seg=12, min_seg=6, mat=ROPE)


def pole(p, a, b, r_lo, r_hi, mat=TIMBER, seg=8):
    """One cut pole swept from `a` to `b`."""
    a, b = Vector(a), Vector(b)
    span = b - a
    rot = Vector((0, 0, 1)).rotation_difference(span.normalized()) \
        .to_matrix().to_4x4()
    p.cyl(tuple((a + b) * 0.5), r_lo, span.length, seg=seg, mat=mat,
          radius_top=r_hi, rot=rot)


# ------------------------------------------------------------------- masts
def build_pole(coll, mats):
    """One pole, tapered, whipped at the head. Nothing else."""
    def part(p):
        pole(p, (0, 0, 0), (0, 0, MAST_LEN + 0.10), 0.042, 0.028)
        whipping(p, MAST_LEN, 0.0284)
    emit(coll, "Mesh_SailRig_MastPole", mats, part)


def build_lashed(coll, mats):
    """Two poles spliced end to end and bound with three turns of hemp - how a
    camp gets a long mast out of short timber."""
    def part(p):
        pole(p, (0, 0, 0), (0, 0, 2.15), 0.052, 0.042)                 # lower
        pole(p, (0, 0, 1.55), (0, 0, MAST_LEN + 0.10), 0.038, 0.026)   # upper
        for z in (1.70, 1.90, 2.05):
            p.torus((0, 0, z), 0.048, 0.013, axis='Z', maj_seg=12, min_seg=6,
                    mat=ROPE)
        whipping(p, MAST_LEN, 0.0266)
    emit(coll, "Mesh_SailRig_MastLashed", mats, part)


def build_tripod(coll, mats):
    """Shear legs: three poles crossed above the tie and bound at the crossing.
    Stands on rock or loose sand where a single pole will not hold."""
    def part(p):
        for i in range(3):
            a = i * 2.0 * math.pi / 3.0
            pole(p, (0.62 * math.sin(a), -0.62 * math.cos(a), 0.0),
                 (0.085 * math.sin(a + math.pi), -0.085 * math.cos(a + math.pi),
                  MAST_LEN + 0.22), 0.034, 0.024)
        p.torus((0, 0, 3.36), 0.070, 0.015, axis='Z', maj_seg=12, min_seg=6,
                mat=ROPE)                                # binding at the cross
        whipping(p, MAST_LEN, 0.050, minor=0.013)
    emit(coll, "Mesh_SailRig_MastTripod", mats, part)


def build_stub(coll, mats):
    """A short stout post for a corner pulled down near the ground."""
    def part(p):
        pole(p, (0, 0, 0), (0, 0, STUB_LEN + 0.08), 0.052, 0.040)
        whipping(p, STUB_LEN, 0.0407, minor=0.013)
    emit(coll, "Mesh_SailRig_MastStub", mats, part)


# ----------------------------------------------------------------- anchors
def build_stake(coll, mats):
    """A driven peg with one rope turn under its head. The default tie-down."""
    def part(p):
        pole(p, (0, 0, 0), (0, 0, 0.28), 0.026, 0.017)
        p.torus((0, 0, 0.215), 0.028, 0.010, axis='Z', maj_seg=10, min_seg=6,
                mat=ROPE)
    emit(coll, "Mesh_SailRig_AnchorStake", mats, part, bevel=0.003)


def build_cleat(coll, mats):
    """A deadman: a board bedded in the sand, pinned by two stakes, with a rope
    loop over it. For ground a stake alone would pull straight out of."""
    def part(p):
        p.slab((-0.155, -0.044, 0.0), (0.155, 0.044, 0.046), mat=PLY)
        for s in (-1, 1):
            pole(p, (s * 0.105, 0, 0.0), (s * 0.105, 0, 0.145), 0.016, 0.011)
        p.torus((0, 0, 0.046), 0.048, 0.011, axis='Y', maj_seg=10, min_seg=6,
                mat=ROPE)
    emit(coll, "Mesh_SailRig_AnchorCleat", mats, part, bevel=0.003)


def build_log(coll, mats):
    """A short log laid on the sand with rope round it - ballast, not a pin."""
    def part(p):
        p.cyl((0, 0, 0.062), 0.062, 0.34, axis='Y', seg=8, mat=TIMBER)
        p.torus((0, 0.055, 0.062), 0.072, 0.012, axis='Y', maj_seg=10,
                min_seg=6, mat=ROPE)
    emit(coll, "Mesh_SailRig_AnchorLog", mats, part, bevel=0.004)


def main():
    out = bl.parse_out()
    bl.start(out)
    mats = bl.link_materials(MATS)
    root = bpy.context.scene.collection

    build_pole(bl.collection("Coll_SailRig_MastPole", root), mats)
    build_lashed(bl.collection("Coll_SailRig_MastLashed", root), mats)
    build_tripod(bl.collection("Coll_SailRig_MastTripod", root), mats)
    build_stub(bl.collection("Coll_SailRig_MastStub", root), mats)
    build_stake(bl.collection("Coll_SailRig_AnchorStake", root), mats)
    build_cleat(bl.collection("Coll_SailRig_AnchorCleat", root), mats)
    build_log(bl.collection("Coll_SailRig_AnchorLog", root), mats)

    bl.report()
    bl.save(out)


if __name__ == "__main__":
    main()
