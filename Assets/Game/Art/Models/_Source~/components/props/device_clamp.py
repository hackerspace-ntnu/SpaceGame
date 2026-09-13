"""Device clamp — how a piece of issued equipment gets fixed to something else.

The strap-on booster is the first user, but nothing here is about rockets: a
cradle pad, a pair of biting jaws and a ratchet strap are what any of this kit
needs to be attached to a crate, a hull plate, a saddle rail or a person's back.

Four variations, differing in how they hold rather than in colour:

  Pad      a concave rubber-faced cradle. Seats on flat and gently curved
           surfaces alike, which is the honest answer when the target could be a
           crate, a hull or a ribcage.
  Jaw      a fixed hook and a HINGED counter-jaw, authored OPEN. Bites an edge.
  Strap    a webbing band that wraps the device and carries on past it, with a
           ratchet buckle. This is the part that makes "strapped on" read from
           across a room — the jaws are small and the pad is hidden underneath.
  Magnet   a pole-faced puck for a bare hull plate. Built ahead; nothing uses it.

Orientation and origin
----------------------
The Pad, the Jaw and the Magnet all **press along −Z**: their contact plane is
z = 0 and their hardware stands above it, so a model attaches one by putting its
origin on the surface it grips and turning that surface's outward normal onto −Z.

The Strap is the exception and says so: its origin is the **axis of the thing it
wraps**, because a band is placed by the cylinder it goes round, not by a face.

The hinged jaw carries its pivot in its own object origin — one local X rotation
is the whole bite, which is the same call `dragon_bazooka.py` made for its jaw and
`sucker_puncher.py` for its ram. An armature for one rigid part turning about one
axis is dead weight.

    blender --background --python device_clamp.py -- --out device_clamp.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)
sys.path.insert(0, HERE)

from _buildlib import collection, link_materials, parse_out, report, save, start  # noqa: E402
from _tracked import TrackedPart  # noqa: E402
from device_kit import (BEVEL_SEG, BEVEL_W, MATS, SEG, AMBER, CANVAS,  # noqa: E402
                        CHROME, DARK, GREY, RUBBER, STEEL)

# --- the numbers other files dock against -----------------------------------
# A second copy of any of these is how a jaw ends up 8 mm too shallow to bite
# with nothing in either file looking wrong.

PAD_X = 0.110           # across the cradle
PAD_Y = 0.200           # along it
PAD_Z = 0.026           # backing thickness at the edges
PAD_DIP = 0.011         # how deep the cradle hollows out at its centre
PAD_R = 0.14            # the surface radius the hollow is cut for: flat enough
                        # to sit on a crate lid, curved enough for a 0.28 m hull

JAW_GAP = 0.062         # throat opening, jaw tip to jaw tip when open
JAW_BITE = 0.048        # how deep an edge can go into the throat
JAW_HINGE = (0.0, 0.049, 0.060)     # the moving jaw's pivot, in clamp space
JAW_OPEN = 34.0         # degrees the moving jaw stands open at rest

STRAP_R = 0.056         # radius of the body the band wraps
STRAP_W = 0.026         # webbing width
STRAP_T = 0.005         # webbing thickness
STRAP_ARC = 264.0       # degrees of wrap: enough to read as "around", short
                        # enough that the two free ends are visible


def _emit(p, hard, name, coll, origin=(0, 0, 0)):
    p.restamp()
    p.bevel(hard, width=BEVEL_W, segments=BEVEL_SEG)
    return p.finish(name, coll, origin=origin)


# -- Pad ---------------------------------------------------------------------

def _cradle_arc(half_x, dip, drop=0.0):
    """The hollow underside as a list of (x, z), left to right.

    A parabola rather than a true arc — indistinguishable at this depth and it
    cannot go imaginary when a caller tapers `half_x` in. `drop` lowers the whole
    curve, which is how the rubber facing ends up PROUD of the backing it is set
    into instead of exactly coincident with it.
    """
    n = 9
    return [(-half_x + 2 * half_x * i / n,
             dip * (1.0 - ((2.0 * i / n) - 1.0) ** 2) - drop)
            for i in range(n + 1)]


def _cradle_profile(half_x, dip, drop=0.0, top=PAD_Z):
    """Closed (x, z) outline: hollow underside, flat back at `top`."""
    return _cradle_arc(half_x, dip, drop) + [(half_x, top), (-half_x, top)]


def pad(coll, mats):
    """Concave cradle with a rubber facing sunk into it.

    Two lofts in one object: a grey moulded backing, and a rubber facing 1 mm
    proud of the hollow and 3 mm buried in it. Proud rather than flush because
    two parallel faces landing on one plane is the flicker this library's own
    scripts warn about, and buried rather than resting on it for the same
    reason from the other side.
    """
    p = TrackedPart(mats)

    taper = ((-0.5 * PAD_Y, 0.84), (-0.5 * PAD_Y + 0.014, 1.0),
             (0.5 * PAD_Y - 0.014, 1.0), (0.5 * PAD_Y, 0.84))
    hard = p.loft([(y, _cradle_profile(0.5 * PAD_X * s, PAD_DIP))
                   for y, s in taper], axis='Y', mat=GREY)

    # The facing follows the same hollow, dropped 2.8 mm so it stands clear of
    # the backing, and closed 4 mm above it so its back is buried. Built at the
    # same z as the backing it was, and the two surfaces were exactly coincident
    # — a clash `_zverify` caught and the eye would not have until it flickered.
    p.loft([(y, _cradle_profile(0.5 * PAD_X * s - 0.006, PAD_DIP, 0.0028,
                                top=PAD_DIP + 0.004))
            for y, s in ((-0.5 * PAD_Y + 0.010, 0.90),
                         (0.5 * PAD_Y - 0.010, 0.90))],
           axis='Y', mat=RUBBER)

    # Two bolt bosses on the back, where the device it serves picks it up.
    for sy in (-1, 1):
        hard += p.cyl((0, sy * 0.062, PAD_Z + 0.004), 0.013, 0.012, axis='Z',
                      seg=12, mat=STEEL)
    return _emit(p, hard, "Mesh_DeviceClamp_Pad", coll)


# -- Jaws --------------------------------------------------------------------

def _teeth(p, y, z0, z1, sign, mat):
    """Three ridges on the inside face of a jaw foot, biting toward `sign` Y."""
    for i in range(3):
        z = z0 + (z1 - z0) * (i + 0.5) / 3.0
        p.box((0, y + sign * 0.004, z), (0.026, 0.008, 0.005), mat)


def jaw_fixed(coll, mats):
    """The half that does not move: a shank with a hook foot at its base."""
    p = TrackedPart(mats)
    y = -0.5 * JAW_GAP - 0.007
    hard = p.box((0, y - 0.007, 0.036), (0.032, 0.014, 0.064), STEEL)
    hard += p.box((0, y - 0.001, 0.010), (0.032, 0.026, 0.012), STEEL)
    _teeth(p, y, 0.006, JAW_BITE, +1, DARK)
    # Spine carrying the hook back to the pivot. Without it the shank and the
    # hinge lug are two lumps 0.1 m apart with air between them, which reads as
    # two unrelated fittings rather than as one clamp.
    hard += p.box((0, 0.5 * (y - 0.014 + JAW_HINGE[1]), JAW_HINGE[2] + 0.002),
                  (0.028, JAW_HINGE[1] - y + 0.014, 0.015), STEEL)
    # Hinge lug for the moving jaw's pin, so the pair reads as one mechanism.
    hard += p.cyl((0, JAW_HINGE[1], JAW_HINGE[2]), 0.010, 0.036, axis='Y',
                  seg=12, mat=DARK)
    return _emit(p, hard, "Mesh_DeviceClamp_JawFixed", coll)


def jaw_moving(coll, mats):
    """The half that bites, authored OPEN, with its origin on the pivot.

    Every box is rigid-transformed about the hinge: the CENTRE is rotated and the
    same rotation is handed to `Part.box`. Rotating only the centre leaves a
    box that has moved but not turned; passing only `rot` turns each box about
    its own middle and the jaw comes apart into confetti — the exact failure
    `weapon_grip.py` records for its raked grip.
    """
    p = TrackedPart(mats)
    hinge = Vector(JAW_HINGE)
    rot = Matrix.Rotation(math.radians(JAW_OPEN), 4, 'X')

    def at(pt):
        return hinge + rot.to_3x3() @ (Vector(pt) - hinge)

    y = 0.5 * JAW_GAP + 0.007
    hard = p.box(at((0, y + 0.007, 0.036)), (0.032, 0.014, 0.064), STEEL,
                 rot=rot)
    hard += p.box(at((0, y + 0.001, 0.010)), (0.032, 0.026, 0.012), STEEL,
                  rot=rot)
    for i in range(3):
        z = 0.006 + (JAW_BITE - 0.006) * (i + 0.5) / 3.0
        p.box(at((0, y - 0.004, z)), (0.026, 0.008, 0.005), DARK, rot=rot)

    # Over-centre lever, so the jaw reads as something a hand throws shut.
    hard += p.box(at((0, y + 0.030, 0.062)), (0.020, 0.052, 0.011), CHROME,
                  rot=rot)
    hard += p.cyl(hinge, 0.008, 0.040, axis='Y', seg=12, mat=CHROME)
    return _emit(p, hard, "Mesh_DeviceClamp_JawMoving", coll, origin=JAW_HINGE)


# -- Strap -------------------------------------------------------------------

def strap(coll, mats):
    """A webbing band wrapping a `STRAP_R` body, with a ratchet buckle.

    Laid as a chain of short boxes along the arc, each turned onto the local
    tangent — the same technique `_buildlib.helix` uses, and for the same reason:
    `loft` puts every ring perpendicular to one axis, which turns a band that
    curves in its own plane into a flat ribbon.

    The wrap stops short of a full circle and both ends run on as free tails.
    A closed ring would read as a moulded collar; the whole job of this part is
    to say "something was cinched down here" (`GDC-L1-UX-0004`).
    """
    p = TrackedPart(mats)
    steps = 30
    half = math.radians(STRAP_ARC) / 2.0
    pts = []
    for i in range(steps + 1):
        a = math.pi / 2.0 + (-half + 2 * half * i / steps)
        pts.append(Vector((0.0, STRAP_R * math.cos(a), STRAP_R * math.sin(a))))

    for a, b in zip(pts, pts[1:]):
        d = b - a
        turn = Vector((0, 1, 0)).rotation_difference(d.normalized()) \
                                .to_matrix().to_4x4()
        p.box((a + b) / 2.0, (STRAP_W, d.length * 1.25, STRAP_T), CANVAS,
              rot=turn)

    # Free tails: the band carries on past the wrap, down and slightly out.
    for end, sign in ((pts[0], -1), (pts[-1], 1)):
        d = Vector((0.0, sign * 0.012, -0.052))
        turn = Vector((0, 1, 0)).rotation_difference(d.normalized()) \
                                .to_matrix().to_4x4()
        p.box(end + d / 2.0, (STRAP_W, d.length, STRAP_T), CANVAS, rot=turn)

    hard = p.box((0, 0, STRAP_R + 0.010), (0.036, 0.052, 0.018), DARK)
    hard += p.box((0, 0, STRAP_R + 0.021), (0.014, 0.030, 0.009), CHROME)
    return _emit(p, hard, "Mesh_DeviceClamp_Strap", coll)


# -- Magnet (built ahead) ----------------------------------------------------

def magnet(coll, mats):
    """Pole-faced puck for a bare hull plate. Nothing uses it yet."""
    p = TrackedPart(mats)
    hard = p.cyl((0, 0, 0.019), 0.045, 0.026, axis='Z', seg=SEG, mat=GREY)
    p.tube((0, 0, 0.004), 0.043, 0.010, 0.008, axis='Z', seg=SEG, mat=CHROME)
    p.cyl((0, 0, 0.004), 0.020, 0.008, axis='Z', seg=SEG, mat=DARK)
    p.cyl((0, 0.030, 0.034), 0.007, 0.006, axis='Z', seg=10, mat=AMBER)
    hard += p.cyl((0, 0, 0.038), 0.016, 0.014, axis='Z', seg=12, mat=STEEL)
    return _emit(p, hard, "Mesh_DeviceClamp_Magnet", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    pad(collection("Coll_DeviceClamp_Pad"), mats)
    jaws = collection("Coll_DeviceClamp_Jaw")
    jaw_fixed(jaws, mats)
    jaw_moving(jaws, mats)
    strap(collection("Coll_DeviceClamp_Strap"), mats)
    magnet(collection("Coll_DeviceClamp_Magnet"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
