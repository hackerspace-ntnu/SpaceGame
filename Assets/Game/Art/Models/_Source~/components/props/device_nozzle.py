"""Device nozzle — the four ways this kit lets something in or out.

A muzzle is the part of a hand device the player actually aims, so it is also the
part that has to say what the device does before the device does it
(`GDC-L1-UX-0004`). Four of them, and no two could be mistaken for each other at a
glance:

  Bell    a flared ROCKET bell — narrow throat, bolt flange, a `t ** 1.7` flare.
          `Coll_SprayerKit_NozzleBell` is a spray cone at 0.18 m across, nearly
          twice a hand booster's whole body; the two are not the same fitting
          and neither substitutes for the other.
  Brass   a slim brass-collared tip. Pressure leaves here, into something.
          Nothing in `sprayer_kit.blend` is a sealed inflator tip.
  Iris    a six-leaf shutter over a wide mouth, authored OPEN. Things come IN
          here. `Mesh_SprayerNozzle_Iris` is a single static mesh; this one is
          six separately hinged leaves, because the vacuum canister's design
          names the irising shutter as a moving part and a one-piece iris
          cannot open.

Orientation and origin
----------------------
Every variation has its origin on the **plane it bolts to**, at y = 0, and works
along **−Y** — the library's forward, and the direction every `Marker_Muzzle` in
this project already points. A device that needs one facing some other way turns
it and checks the result against the matrix rather than against intention.

The iris leaves are the one place in this file where an object carries a
**rotation** instead of having it applied. That is deliberate: each leaf's origin
sits on its own hinge pin and its LOCAL +Z is its hinge axis, so all six close
with the same local rotation, on every machine, with no per-leaf axis maths in
Unity. Baking the arrangement into the mesh would have left six leaves whose
hinge axes all differ and are recoverable only by trigonometry.

    blender --background --python device_nozzle.py -- --out device_nozzle.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)
sys.path.insert(0, HERE)

from _buildlib import collection, link_materials, parse_out, report, save, start  # noqa: E402
from _tracked import TrackedPart  # noqa: E402
from device_kit import (BEVEL_SEG, BEVEL_W, MATS, SEG, BLACK, BRASS, CHROME,  # noqa: E402
                        DARK, GREY, RUBBER, SHELL, STEEL)

# --- numbers other files dock against ---------------------------------------

BELL_THROAT = 0.028     # bore radius where the bell bolts on
BELL_EXIT = 0.058       # radius at the lip
BELL_LEN = 0.078

IRIS_R = 0.070          # the mouth's clear radius — 0.140 across
IRIS_LEAVES = 6
IRIS_OPEN = 82.0        # degrees each leaf stands back at rest


def _emit(p, hard, name, coll, origin=(0, 0, 0)):
    p.restamp()
    p.bevel(hard, width=BEVEL_W, segments=BEVEL_SEG)
    return p.finish(name, coll, origin=origin)


def _ring(r, n=SEG):
    return [(r * math.cos(2 * math.pi * i / n), r * math.sin(2 * math.pi * i / n))
            for i in range(n)]


# -- Bell --------------------------------------------------------------------

def bell(coll, mats):
    """Flared rocket bell: a lofted outer shell, a dark liner, a bolt flange.

    The flare is `t ** 1.7` rather than linear because a straight cone reads as a
    funnel and a bell reads as thrust. Sixteen stations, so the silhouette is
    smooth at the distance a 0.4 m item is held from.
    """
    p = TrackedPart(mats)
    n = 16

    def radius(t, throat, exit_r):
        return throat + (exit_r - throat) * (t ** 1.7)

    hard = p.loft([(-BELL_LEN * i / n, _ring(radius(i / n, BELL_THROAT, BELL_EXIT)))
                   for i in range(n + 1)], axis='Y', mat=STEEL, cap=False)
    p.loft([(-BELL_LEN * i / n + 0.001,
             _ring(radius(i / n, BELL_THROAT - 0.004, BELL_EXIT - 0.005)))
            for i in range(n + 1)], axis='Y', mat=BLACK, cap=False)

    hard += p.tube((0, 0.008, 0), BELL_THROAT + 0.010, 0.008, 0.016, axis='Y',
                   seg=SEG, mat=DARK)
    # Six bolt heads round the flange: the read that says "this is a fitting
    # bolted on", not a shape the casing happens to end in.
    for i in range(6):
        a = 2 * math.pi * i / 6
        p.cyl(((BELL_THROAT + 0.006) * math.cos(a), 0.001,
               (BELL_THROAT + 0.006) * math.sin(a)), 0.005, 0.006, axis='Y',
              seg=6, mat=CHROME)
    return _emit(p, hard, "Mesh_DeviceNozzle_Bell", coll)


# -- Brass -------------------------------------------------------------------

def brass(coll, mats):
    """Inflator tip: brass collar, chrome taper, rubber sealing lip.

    Three materials over 75 mm is a lot for one small part, and it is the point:
    a tip that reads as machined and gasketed says "this presses against
    something and holds pressure" without a word of UI.
    """
    p = TrackedPart(mats)
    hard = p.tube((0, -0.016, 0), 0.016, 0.005, 0.032, axis='Y', seg=SEG,
                  mat=BRASS)
    hard += p.cyl((0, -0.005, 0), 0.019, 0.010, axis='Y', seg=12, mat=BRASS)
    p.cyl((0, -0.047, 0), 0.012, 0.038, axis='Y', seg=SEG, mat=CHROME,
          radius_top=0.008)
    p.tube((0, -0.070, 0), 0.009, 0.003, 0.012, axis='Y', seg=12, mat=RUBBER)
    # Knurl: eight ridges round the collar, so the hand reads it as adjustable.
    for i in range(8):
        a = 2 * math.pi * i / 8
        p.box((0.0163 * math.cos(a), -0.016, 0.0163 * math.sin(a)),
              (0.004, 0.022, 0.004), CHROME,
              rot=Matrix.Rotation(-a, 4, 'Y'))
    return _emit(p, hard, "Mesh_DeviceNozzle_Brass", coll)


# -- Iris --------------------------------------------------------------------

def iris(coll, mats):
    """A six-leaf shutter over a `2 * IRIS_R` mouth, authored OPEN.

    Each leaf is built once in a canonical pose — lying flat in the mouth plane,
    reaching inward from its hinge at `(IRIS_R, 0, 0)` — then swung back about
    that hinge and turned into place around the mouth axis.

    Both halves of that are worth stating, because a wrong one is invisible:

    * The **swing** is about the leaf's own **+Z**, which is the mouth-plane
      tangent at the canonical hinge. A rotation about Z carries +X to +Y, so a
      *negative* angle takes a leaf reaching along −X and lifts it toward +Y —
      back INTO the throat, behind the mouth. A positive angle throws it forward
      out of the device. The sign is the half that goes silently wrong.
    * The **arrangement** is about **Y**, the mouth axis, and it goes into the
      object's `rotation_euler` rather than into the mesh, so every leaf keeps
      the same local hinge axis. See this module's docstring.
    """
    p = TrackedPart(mats)
    hard = p.tube((0, 0.004, 0), IRIS_R + 0.016, 0.014, 0.030, axis='Y',
                  seg=SEG, mat=SHELL)
    hard += p.tube((0, -0.010, 0), IRIS_R + 0.004, 0.006, 0.010, axis='Y',
                   seg=SEG, mat=DARK)
    ring = _emit(p, hard, "Mesh_DeviceNozzle_IrisRing", coll)

    hinge = Vector((IRIS_R, 0.0, 0.0))
    swing = Matrix.Rotation(math.radians(-IRIS_OPEN), 4, 'Z')
    half = math.radians(180.0 / IRIS_LEAVES) * 1.06     # 6% overlap, so a shut
                                                        # iris has no daylight
    leaves = []
    for k in range(IRIS_LEAVES):
        q = TrackedPart(mats)
        # Closed pose: a wedge from the rim in to a small central hole.
        outer, inner = IRIS_R + 0.002, 0.009
        prof = [(outer, -outer * math.tan(half)), (outer, outer * math.tan(half)),
                (inner, inner * math.tan(half)), (inner, -inner * math.tan(half))]
        q.prism(prof, 0.005, axis='Y', mat=DARK)
        # 2.8 mm proud toward the mouth (−Y is out), so the inlay is neither
        # coplanar with the leaf's own face nor floating off it. At the 1.6 mm
        # it was built at, `_zverify` flagged all six leaves — the tool's SEP is
        # 2 mm and it is right to: a 1.6 mm gap flickers at range.
        q.prism([(u * 0.94, v * 0.9) for u, v in prof], 0.005, axis='Y',
                mat=CHROME, offset=(0, -0.0028, 0))
        q.cyl((IRIS_R, 0, 0), 0.006, 0.014, axis='Z', seg=8, mat=STEEL)
        q.restamp()
        leaf = q.finish("Mesh_DeviceNozzle_IrisLeaf_%d" % (k + 1), coll,
                        origin=tuple(hinge))
        leaf.data.transform(swing)
        leaf.rotation_euler = (0.0, 2 * math.pi * k / IRIS_LEAVES, 0.0)
        leaf.location = (Matrix.Rotation(2 * math.pi * k / IRIS_LEAVES, 4, 'Y')
                         @ hinge)
        leaves.append(leaf)

    bpy.context.view_layer.update()
    return ring, leaves


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    bell(collection("Coll_DeviceNozzle_Bell"), mats)
    brass(collection("Coll_DeviceNozzle_Brass"), mats)
    iris(collection("Coll_DeviceNozzle_Iris"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
