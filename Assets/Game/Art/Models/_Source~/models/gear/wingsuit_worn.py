"""Build wingsuit_worn.blend — the wingsuit as it looks WORN, not stowed.

`wingsuit.blend` is the flight suit: a slim spar case on the lash rail, two spar
ends past the flanks, and two membranes that Unity straps to the arm bones when
the wings deploy. Worn, all you ever saw was the case — a box on somebody's
back, which says nothing about what the item is.

This is the other thing the same item has to be: **the wings shown between the
arms**, on a figure standing in a T-pose. Two cloth panels running from each
shoulder out along the arm and down toward the hip, carried on an over-shoulder
yoke that laces back to the rail.

Gone, deliberately: the spar case, the two clamps and the two spar stubs. All
three exist to describe a wing that is FOLDED AWAY, and there is nothing folded
away here.

## The wearer, measured — this is why every number below is what it is

Authored in the WEARER's frame at true scale, origin **on the spine bone**, the
bone `WornSeat` seats a torso item on: +X the wearer's LEFT, +Z up, −Y forward.
(Unity's frame is `(x, y, z) -> (-x, z, -y)` of this, so +X here is Unity −X,
which is the wearer's left there too.)

Taken off the game rather than guessed — `PlayerCharacter.prefab`'s bind pose,
read through the skinned mesh's bind matrices, 2026-09-03 — in this file's own
axes, metres:

    upper arm joint   (±0.233,  0.012,  0.637)   the wing's leading-edge root
    hip joint         (±0.143, -0.029, -0.269)   how far the cloth may fall
    clavicle top      (±0.075,  0.003,  0.712)   what the yoke arcs over
    neck              ( 0.000,  0.000,  0.690)
    lash rail         ( 0.000,  0.522,  0.630)   where the yoke laces back to
    shoulder to wrist  0.864 along the arm

**The arm line is a 45-degree A, not a T** (user, 2026-09-03: "the T pose needs
to be much more low key, I am thinking more like 45 degrees"), and the wing is
authored along it. `BodyFocusSession` stands the wearer at the same angle while
the gear screen is open, so the model and the screen agree by construction; the
one number lives in `INSPECT_DROOP` below and on `BodyFocusSession.armDroop`.

Lowering the arm is not free, and `SWEEP` is what pays for it. A loft's sections
are PERPENDICULAR to its span, so with the arm at 45 degrees the perpendicular
chord runs 45 degrees inboard as well as down — and the cloth's lower edge walks
straight into the torso within about 10 cm. A wingsuit's arm wing does not do
that: its free edge runs from the WRIST to the HIP, so the chord line is raked
outboard, not square. `SWEEP` shears the lofted panel along its own span by that
rake, which is what puts the root's trailing edge on the wearer's flank instead
of inside their ribs.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.

    blender --background --python models/gear/wingsuit_worn.py -- \
        --out models/gear/wingsuit_worn.blend

    # look at it without writing anything:
    blender --background models/gear/wingsuit_worn.blend --python ../../_preview.py -- \
        --out /tmp/worn.png --view front
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, _HERE)

import bpy  # noqa: E402
from _buildlib import *  # noqa: E402,F403
from _wingsuit import Wing  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

_ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(flag, default=None):
    """Read `--flag <value>` from the args after `--`.

    The shape knobs below are exposed this way so the wing can be previewed at a
    few droops and rakes into a scratch .blend before one is committed — the
    committed file refuses to be overwritten, and rightly.
    """
    return _ARGV[_ARGV.index(flag) + 1] if flag in _ARGV else default


# STEEL is deliberately index 0: `Part.bevel` stamps material index 0 onto every
# face it creates, so whatever sits first is what all the softened edges come out
# as. The flight suit shipped its clamp corners as beige sailcloth exactly once
# by putting the cloth first — see `wingsuit_BUILD.md`.
STEEL, DARK, CLOTH, CANVAS, STRAP, BRASS = range(6)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark", "Mat_Fabric_Wing_Beige",
        "Mat_Fabric_Canvas_Sand", "Mat_Fabric_Canvas_Faded",
        "Mat_Metal_Brass_Tarnished"]

# --------------------------------------------------------------- the wearer
SHOULDER = Vector((0.233, 0.012, 0.637))    # mirrored in x per side
RAIL = Vector((0.000, 0.522, 0.630))
HIP_Z = -0.269
ARM_LENGTH = 0.864

# --------------------------------------------------------------- the wing
# "A bit folded so the T pose is not truly extended" (user, 2026-09-03), spent
# on three things rather than on drooping the arm line:
#
#   SPAN_FRACTION  the cloth runs out at 0.92 of the arm, so the wrist is bare —
#                  a spread wing reaches the cuff, a folded one does not.
#   BACK_TILT      the whole panel is rolled aft about the arm axis, so the
#                  cloth hangs BEHIND the arm rather than standing in the
#                  wearer's own plane. See the constant: it is also what keeps
#                  the inboard corner out of the wearer's waist, and it is
#                  bounded above by the gear screen's head-on camera — a wing
#                  seen edge-on is a line.
#   CHORD_FALLOFF  steeper than the flight suit's 1.35, so the trailing edge
#                  sweeps back sharply instead of running out as a long triangle
#                  — gathered cloth, not stretched cloth.
#
# The station count is not a budget decision either. At the flight suit's 9x6
# and this steeper falloff the trailing edge came out visibly SAWTOOTHED — nine
# flat quads across a curve that now turns hard near the root, which reads as
# a notched hem rather than as cloth. 20x10 is where the teeth disappear, and
# it costs about 700 triangles a panel.
# How far below horizontal the wearer's arms hang, degrees. The wing's leading
# edge is authored along exactly this line, and `BodyFocusSession.armDroop` is
# set to the same number — the two have to agree or the cloth floats off the arm.
INSPECT_DROOP = float(arg("--droop", 45.0))

SPAN_FRACTION = 0.92
# Degrees the whole panel is rolled AFT about the arm axis.
#
# 18 rather than the 10 the horizontal-armed version used, and the extra eight
# are structural. With the arm down at 45 the panel's inboard corner sits beside
# the wearer's waist; the roll is what carries it BEHIND them instead, out past
# the small of the back. Measured, not eyeballed: at 10 that corner lands at
# (0.106, 0.136) — inside a torso whose half-width is about 0.20 — and at 18 it
# lands at (0.179, 0.229), clear behind the hip.
BACK_TILT = float(arg("--tilt", 18.0))
ROOT_CHORD = float(arg("--root-chord", 0.60))
TIP_CHORD = 0.13
CAMBER = 0.090       # slack, not taut

# How far the lofted panel's trailing edge is raked OUTBOARD along the span, as a
# fraction of the local chord. A shear, applied to the finished mesh.
#
# It is what makes a lowered arm possible at all. At 45 degrees the perpendicular
# chord direction is (-0.707, 0, -0.707) in the wearer's frame — down AND inboard
# in equal measure — so 0.60 m of square chord would put the root's trailing edge
# at x = 0.11, inside a torso whose half-width is about 0.20. Raked, that corner
# lands on the flank instead, and the free edge runs wrist-to-hip the way a real
# arm wing's does.
#
# It has a ceiling, and the ceiling is geometric rather than aesthetic: the
# trailing edge's own x is `x + SWEEP * chord(x)`, so it stops advancing when
# `SWEEP * |chord'(x)|` reaches 1 and the panel folds back through itself. With
# this chord curve (falloff 1.25) that is 1.35; 1.00 keeps a comfortable margin,
# and the shipped panel's trailing edge still advances at 0.26 m per metre of span.
SWEEP = float(arg("--sweep", 1.00))

# How far the leading edge is set off the arm, along the chord, metres.
#
# Not a look decision — an occlusion one, and it was measured off a render. This
# astronaut's upper arm is about 0.12 m in radius, so a leading edge ON the arm
# line puts the cloth inside it and the wing disappears from the one angle that
# matters (the gear screen looks at the character head on). Set off a little, the
# whole triangle is visible and the batten still reads as strapped to the arm.
#
# Along the CHORD rather than along world down, so it stays correct at any droop.
LEADING_DROP = float(arg("--drop", 0.110))

# How much bigger than the wearer's own arm the wing is built, 2026-09-04.
#
# The panel used to end AT the arm — span `ARM_LENGTH * SPAN_FRACTION`, the cuff
# on the wrist — which is why it read as small on the gear screen: a wingsuit
# whose wing is exactly as long as a forearm is a sleeve. The user asked for
# twice as big and chose, explicitly, the reading where the cloth runs out PAST
# the hands rather than the one where only the chord deepens.
#
# It scales the WING and nothing else: span, both chords, camber, skin and the
# spar it is stretched over. The yoke, the shoulder straps and the cuff are
# fitted to a BODY that did not change size, so they are untouched — this is
# the whole reason the enlargement is a constant on the wing rather than a
# scale on the file. See CUFF_SPAN for the one number that has to know both.
WING_SCALE = float(arg("--wing-scale", 2.0))

WORN_WING = Wing(span=ARM_LENGTH * SPAN_FRACTION * WING_SCALE,
                 root_chord=ROOT_CHORD * WING_SCALE,
                 tip_chord=TIP_CHORD * WING_SCALE,
                 camber=CAMBER * WING_SCALE, chord_falloff=1.25,
                 skin_root=0.015 * WING_SCALE, skin_tip=0.008 * WING_SCALE,
                 span_stations=20, chord_points=10)

# Where along the spar the cuff sits: the END OF THE ARM, not the end of the
# wing, and at WING_SCALE 1 those were the same point.
#
# The cuff is a webbing wrap round the FOREARM — it is the part that says the
# wing is strapped to a limb. Carried out to the wing's own tip it would be a
# forearm wrap closed round thin air 0.8 m past the wearer's hand. So it stays
# where the arm ends and reads as the spar's mid-span anchor, which is what a
# spar longer than its arm actually needs.
CUFF_SPAN = ARM_LENGTH * SPAN_FRACTION

BATTEN_ROOT_R = 0.026 * WING_SCALE
BATTEN_TIP_R = 0.013 * WING_SCALE


def spar_radius_at(fraction):
    """The tapered spar's radius `fraction` of the way out, metres.

    The cuff is no longer at the tip, so it cannot be sized off `BATTEN_TIP_R`
    the way it was — at WING_SCALE 2 that ferrule would be visibly narrower than
    the spar it is clamped around, which is the kind of wrong that reads as a
    modelling mistake rather than as a design.
    """
    return BATTEN_ROOT_R + (BATTEN_TIP_R - BATTEN_ROOT_R) * fraction


def side_basis(side_sign):
    """The world basis a membrane built in `_wingsuit`'s frame is placed with.

    Columns are the membrane's own X (outboard along the arm), Y and Z. The
    chord runs along the membrane's **−Y**, so Y is chosen to point up and the
    chord therefore falls; Z is then forced by right-handedness.

    Right-handed on BOTH sides — determinant +0.999, asserted below. A wing
    placed by a negative scale is the trap the gauntlet family documented: it
    inverts winding, and Unity draws the mesh inside-out with a clean console.
    Two real meshes and two real bases cost 500 triangles and no ambiguity.

    The price of not mirroring is that `Z` comes out pointing FORWARD on one
    side and AFT on the other, so the camber has to be signed per side to bow
    both panels the same way in the world. `Wing` documents that sign; this is
    the caller that needs it.
    """
    tilt = math.radians(BACK_TILT)
    droop = math.radians(INSPECT_DROOP)

    # Along the arm: outboard and down by the droop.
    x = Vector((side_sign * math.cos(droop), 0.0, -math.sin(droop)))

    # Perpendicular to it in the wearer's own plane, pointing UP — so the chord,
    # which runs along the membrane's −Y, falls. Then rolled aft by the tilt.
    up = Vector((side_sign * math.sin(droop), 0.0, math.cos(droop)))
    aft = Vector((0.0, 1.0, 0.0))
    y = (up * math.cos(tilt) - aft * math.sin(tilt)).normalized()
    z = x.cross(y)
    basis = Matrix((x, y, z)).transposed().to_4x4()
    det = basis.to_3x3().determinant()
    if det < 0.9:
        raise SystemExit("side %+d basis is not right-handed (det %.4f)"
                         % (side_sign, det))
    return basis


def camber_sign(side_sign):
    """Which way `Wing.camber` has to point so both panels bow AFT.

    `side_basis`'s Z is `X x Y`; with X flipped between sides, Z flips too. The
    wearer's left gets Z forward, so its camber is negative.
    """
    return -side_sign


def root(side_sign):
    """Where one wing's leading edge starts: the shoulder joint, set off along
    the chord so the cloth clears the arm. Every part of that side is placed from
    this one point, so nothing can drift away from anything else."""
    shoulder = Vector((side_sign * SHOULDER.x, SHOULDER.y, SHOULDER.z))

    # The chord direction is the basis's −Y, so the set-off is simply a step
    # along it. Reading it off the basis rather than typing a world offset is
    # what keeps this correct when the droop changes.
    chord = -Vector(side_basis(side_sign).col[1][:3])
    return shoulder + chord * LEADING_DROP


def place(obj, basis, at):
    """Bake `basis` into the mesh and seat the object at `at`.

    Baked rather than left on the object, because the library's convention is
    transforms applied and scale 1.0 — and because a rotation left on the object
    is a rotation the FBX has to carry as a node transform, which is one more
    thing that can arrive different from how it looked here.
    """
    obj.data.transform(basis)
    obj.location = at
    obj.rotation_euler = (0.0, 0.0, 0.0)


def build_membrane(coll, mats, name, side_sign):
    """One cloth panel, lofted along the arm and rolled aft."""
    part = Part(mats)
    wing = Wing(span=WORN_WING.span, root_chord=WORN_WING.root_chord,
                tip_chord=WORN_WING.tip_chord,
                camber=WORN_WING.camber * camber_sign(side_sign),
                chord_falloff=WORN_WING.chord_falloff,
                skin_root=WORN_WING.skin_root, skin_tip=WORN_WING.skin_tip,
                span_stations=WORN_WING.span_stations,
                chord_points=WORN_WING.chord_points)
    part.loft(wing.sections(), axis='X', mat=CLOTH, cap=True)

    # No bevel. A wing's free edge is a sewn hem, not a rolled fillet, and
    # bevelling it would stamp material 0 — steel — onto the cloth.
    obj = part.finish(name, coll, origin=(0, 0, 0))

    # The rake. A shear rather than a differently-shaped loft, because a loft's
    # sections are perpendicular to its span by construction and no arrangement
    # of them can tilt a chord line. The chord runs along −Y, so +x per unit −y
    # is a −k in the (0, 1) slot.
    shear = Matrix.Identity(4)
    shear[0][1] = -SWEEP
    obj.data.transform(shear)

    place(obj, side_basis(side_sign), root(side_sign))
    return obj


def build_batten(coll, mats, name, side_sign):
    """The leading-edge spar the cloth is stretched over.

    Set slightly PROUD of the leading edge rather than flush with it: a tube
    whose surface is tangent to the panel's would be two coincident surfaces
    down the whole span.
    """
    part = Part(mats)
    length = WORN_WING.span * 0.99
    part.cyl(center=(length * 0.5, 0.018, 0.0),
             radius=BATTEN_ROOT_R, radius_top=BATTEN_TIP_R,
             depth=length, axis='X', seg=10, mat=STEEL)

    # Two collars along the spar, sunk into it so nothing meets flush.
    for f in (0.34, 0.68):
        part.cyl(center=(length * f, 0.018, 0.0), radius=BATTEN_ROOT_R * 1.22,
                 depth=0.026, axis='X', seg=10, mat=BRASS)

    obj = part.finish(name, coll, origin=(0, 0, 0))
    place(obj, side_basis(side_sign), root(side_sign))
    return obj


def build_cuff(coll, mats, name, side_sign):
    """The cuff at the outboard end, where the spar and the hem run out.

    The one part that says the wing STOPS here rather than being cropped: a
    tapered ferrule, a webbing wrap round the forearm, and the buckle closing it.
    """
    part = Part(mats)

    # Sized off the spar where the cuff actually sits rather than off its tip;
    # the two parted company when the wing grew past the arm. Still a taper, so
    # the ferrule reads as gripping the spar rather than as a collar on it.
    spar_r = spar_radius_at(CUFF_SPAN / WORN_WING.span)
    part.cyl(center=(0.0, 0.014, 0.0), radius=spar_r * 1.7,
             radius_top=spar_r * 1.15, depth=0.070, axis='X', seg=10,
             mat=STEEL)
    part.tube(center=(0.012, 0.0, 0.0), radius=0.062, thickness=0.014,
              depth=0.052, axis='X', seg=14, mat=STRAP)
    part.box(center=(0.012, -0.058, 0.0), size=(0.030, 0.034, 0.020),
             mat=BRASS)

    part.bevel(width=0.004, segments=2)
    obj = part.finish(name, coll, origin=(0, 0, 0))

    # Stepped along the basis's OWN span axis, not along world +X. The two were
    # the same thing while the arm line was horizontal, and the moment it drooped
    # to 45 degrees both cuffs flew off sideways, level with the shoulders and a
    # foot clear of the wing they belong to.
    basis = side_basis(side_sign)
    span = Vector(basis.col[0][:3]) * CUFF_SPAN
    place(obj, basis, root(side_sign) + span)
    return obj


def bezier(p0, p1, p2, p3, n):
    """A cubic sampled at `n` points. Straps arc; polylines elbow.

    The first cut routed each shoulder strap through four measured points and
    read as four rigid links with visible corners, not as webbing over a
    shoulder. The measured points became the control hull instead.
    """
    p0, p1, p2, p3 = (Vector(p) for p in (p0, p1, p2, p3))
    out = []
    for i in range(n):
        t = i / (n - 1.0)
        u = 1.0 - t
        out.append(u * u * u * p0 + 3 * u * u * t * p1
                   + 3 * u * t * t * p2 + t * t * t * p3)
    return out


def strap(part, pts, mat, width=0.052, depth=0.020, axis='X'):
    """A webbing run through a polyline, with its segments OVERLAPPED.

    `Part.seam` gives one straight strip; a chain of them leaves a wedge of
    nothing at every kink. The first fix put a cylinder at each interior point,
    which fixed the gap and left the strap looking beaded — a filler sized to
    the strap's WIDTH stands far proud of its much smaller DEPTH.

    So instead each segment is run a little long at both ends, which makes
    consecutive strips interpenetrate. That is the library's own rule for
    touching parts (embed, never abut) applied to the strap's own joints, and it
    adds no geometry.
    """
    pts = [Vector(p) for p in pts]
    over = depth * 0.75
    for a, b in zip(pts, pts[1:]):
        d = (b - a).normalized()
        part.seam(a - d * over, b + d * over, width=width, depth=depth,
                  axis=axis, mat=mat)


def build_yoke(coll, mats):
    """The plate on the lash rail that the two shoulder straps lace back to.

    Slim on purpose. The user's standing note on worn gear is that the DEVICE
    should be big and obvious and the strapping quiet (see the leash, 2026-09-02)
    — here the wings are the device, so the thing holding them on says as little
    as it can get away with while still being visibly load-bearing.
    """
    part = Part(mats)

    hw, hd, hh = 0.170, 0.042, 0.130

    def profile(sy, sz, inset):
        y, z = hd * sy, hh * sz
        c = inset
        return [
            (-y + c, -z), (y - c, -z), (y, -z + c), (y, z - c),
            (y - c, z), (-y + c, z), (-y, z - c), (-y, -z + c),
        ]

    part.loft([
        (-hw, profile(0.66, 0.62, 0.016)),
        (-hw * 0.55, profile(0.92, 0.92, 0.020)),
        (0.0, profile(1.0, 1.0, 0.022)),
        (hw * 0.55, profile(0.92, 0.92, 0.020)),
        (hw, profile(0.66, 0.62, 0.016)),
    ], axis='X', mat=DARK, cap=True)

    # A canvas facing SUNK into the plate's aft face rather than laid on it: a
    # panel exactly on the surface it covers is two coincident faces.
    part.slab((-hw * 0.80, hd - 0.010, -hh * 0.66),
              (hw * 0.80, hd + 0.016, hh * 0.66), mat=CANVAS)

    # The two brass eyes the shoulder straps leave through, buried in the plate.
    for sx in (-1, 1):
        part.cyl(center=(sx * hw * 0.62, -hd * 0.4, hh * 0.52), radius=0.020,
                 depth=hd * 1.6, axis='Y', seg=10, mat=BRASS)

    part.bevel(width=0.005, segments=2)
    obj = part.finish("Mesh_WingsuitWorn_Yoke", coll, origin=(0, 0, 0))
    obj.location = RAIL
    return obj


def build_shoulder_strap(coll, mats, name, side_sign):
    """One webbing run: rail, up over the clavicle, forward to the wing root.

    Routed over the MEASURED clavicle rather than in a straight line, because a
    straight line from the rail to the shoulder joint passes through the trunk.
    """
    part = Part(mats)
    end = root(side_sign)
    route = bezier(
        (side_sign * 0.105, RAIL.y - 0.026, RAIL.z + 0.066),
        (side_sign * 0.156, 0.330, 0.760),
        (side_sign * 0.224, 0.128, 0.768),
        (end.x, end.y + 0.020, end.z + 0.030),
        9)
    strap(part, route, STRAP, width=0.052, depth=0.016, axis='X')

    # The buckle on the run, over the shoulder where a wearer could reach it.
    # Seated ON a sampled point of the arc rather than at a typed position, so
    # it cannot drift off the webbing when the route is tuned.
    part.box(center=tuple(route[4]), size=(0.038, 0.052, 0.026), mat=BRASS)

    part.bevel(width=0.004, segments=2)
    return part.finish(name, coll, origin=(0, 0, 0))


def main():
    out = parse_out()
    start(out)

    mats = link_materials(MATS)
    root = collection("Coll_WingsuitWorn")

    build_yoke(root, mats)

    for side_sign, side in ((1, "L"), (-1, "R")):
        build_shoulder_strap(root, mats,
                             "Mesh_WingsuitWorn_Strap_%s" % side, side_sign)
        build_batten(root, mats, "Mesh_WingsuitWorn_Batten_%s" % side, side_sign)
        build_membrane(root, mats,
                       "Mesh_WingsuitWorn_Membrane_%s" % side, side_sign)
        build_cuff(root, mats, "Mesh_WingsuitWorn_Cuff_%s" % side, side_sign)

    bpy.context.view_layer.update()

    lo = [1e9] * 3
    hi = [-1e9] * 3
    for obj in bpy.data.objects:
        if obj.type != 'MESH':
            continue
        m = obj.matrix_world
        for v in obj.data.vertices:
            w = m @ v.co
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
    print("  worn wingsuit: span %.3f m, depth %.3f m, height %.3f m"
          % (hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]))
    print("  bounds lo (%.3f, %.3f, %.3f)  hi (%.3f, %.3f, %.3f)"
          % (*lo, *hi))
    print("  cloth stops %.3f m above the hip joint" % (lo[2] - HIP_Z))
    print("  arm line droops %.1f degrees; trailing edge raked %.2f" % (INSPECT_DROOP, SWEEP))

    save(out)
    report()


main()
