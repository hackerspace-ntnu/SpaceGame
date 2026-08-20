"""Rebuild the Vrescal's body, rig and skinning around the author's head sculpt.

**This is a one-shot rebuild and it is destructive.** It keeps
`Mesh_Vrescal_Skull`, `Mesh_Vrescal_Jaw` and the two eyes -- the hand-sculpted
pieces -- and deletes everything else in the file: the old body, the fourteen
dorsal plates, the legs, the armature and all six actions. It refuses to run
against a file that does not look like the pre-rebuild Vrescal, so it cannot
quietly eat a second pass of work. The pre-rebuild file is at
`Assets/Game/Art/Models/_backups~/vrescal_before_rebuild_2026-08-15.blend`.

## What the animal is now

A tall, heavy, deep-bodied desert quadruped built to the reference: 7.4 m nose
to tail, 4.6 m at the front hump, twin humps over a barrel ribcage, columnar
legs carrying the belly 1.75 m off the sand, and a long S-curved neck holding
the author's sculpted head at 3.5 m -- player eye level rather than ankle
height, where the old low-slung crocodile put it.

## How it is built, and why it is built that way

The body starts as a lofted tube of elliptical sections -- good for pinning down
a silhouette, useless on its own. **A loft has no anatomy.** No shoulder mass,
no ribcage, no crease where a limb leaves the flank, and a surface so even it
reads as extruded plastic. The first attempt at this rebuild stopped there and
scattered separate armour lumps over the top, and the result looked exactly like
what it was: a smooth tube with potatoes glued to it.

So the loft is only the beginning. `vrescal_surface` then runs four passes over
it, and those passes are where the animal actually comes from:

  MUSCLES   ellipsoidal masses displaced along the normal -- scapula, triceps,
            pectoral, gluteal, quadriceps, ribcage, throat, belly.
  FOLDS     the same machinery with negative strength, plus ring-shaped creases
            for neck wrinkles. Skin gathers where limbs meet the body.
  noise     low-amplitude turbulence, because nothing organic is smooth.
  mosaic    the armour: a Voronoi tessellation *of the body surface*, each cell
            lifted into a plate with a wall dropping back to the hide. The gaps
            between cells show skin, which is the pale crackle between the dark
            scutes in the reference.

The armour being surface-derived rather than scattered is the load-bearing
idea. It cannot float, cannot intersect its neighbours, follows every curve of
the body for free, and deforms perfectly because it is welded into the same
skinned mesh and inherits the weights of the vertices it was copied from.

## Two other things that matter

**Weights come from arc position along the bone chain, not 3-D distance.** A
proximity solve looks right until you try it on a body whose radius (3.7 units)
exceeds the spacing between its spine bones: a belly vertex ends up
near-equidistant from three bones, gets a third of each, and the trunk turns to
rubber. Projecting onto the chain and blending only across a joint's `BLEND`
window gives each vertex at most two bones and a predictable falloff.

**The head is moved, never modified.** It keeps its mesh, materials and scale
exactly; the rebuild only re-parents it to the new `Bone_Head`. The body is laid
out *around* where the sculpt already sits, which is why the tables below start
at the nose and work backwards.

## Geometry, in working units

Worked at 27.0 units nose-to-tail and shipping at 7.45 m, so the export factor
stays 0.2759 -- the same one the old model used, which means the author's skull
is exactly the size it has always been. Ground is the plane z = -13.0.

    nose            x = +2.51   (fixed by the sculpt)
    head rear       x = -2.33   (fixed by the sculpt)
    chest           x = -8.00,  z = -0.60 .. -5.30
    withers         x = -10.10, top z = +1.60
    hump 1 peak     x = -12.10, top z = +3.67   <- 4.6 m, the tallest point
    hump 2 peak     x = -16.90, top z = +2.60
    hip             x = -19.30
    tail tip        x = -24.50
    belly           z = -6.66                   <- 1.75 m of clearance
    sole plane      z = -13.00

    blender --background vrescal.blend --python vrescal_rebuild.py
"""

import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)
sys.path.insert(0, HERE)

import _buildlib as B  # noqa: E402
import vrescal_surface as S  # noqa: E402

COMPONENTS = os.path.join(LIB, "components", "organic")
FEET_BLEND = os.path.join(COMPONENTS, "foot_pad.blend")

KEEP = {"Mesh_Vrescal_Skull", "Mesh_Vrescal_Jaw",
        "Mesh_Vrescal_EyeP", "Mesh_Vrescal_EyeS"}

MATS = ["Mat_Hide_Dune_Tan", "Mat_Hide_Scute_Umber",
        "Mat_Hide_Slate_Teal", "Mat_Hide_Claw_Horn"]
HIDE, PLATE, BELLY, HORN = 0, 1, 2, 3

FOOT_MAT_MAP = {0: BELLY, 1: HORN}

UNITS_PER_M = 3.62450       # 1 / 0.2759, the export factor this file ships at
GROUND = -13.0
RING = 14                   # points around the body loft
BLEND = 0.55                # arc-length window either side of a joint
SUBSURF = 2                 # levels applied to the hide before the mosaic


# --------------------------------------------------------------------------
# Skeleton
# --------------------------------------------------------------------------

SPINE = [
    ("Bone_Pelvis",   (-22.30, 0.0, -1.40), (-19.40, 0.0, -1.00)),
    ("Bone_Spine_01", (-19.40, 0.0, -1.00), (-16.60, 0.0, -0.80)),
    ("Bone_Spine_02", (-16.60, 0.0, -0.80), (-13.80, 0.0, -0.85)),
    ("Bone_Spine_03", (-13.80, 0.0, -0.85), (-10.30, 0.0, -1.70)),
    ("Bone_Neck_01",  (-10.30, 0.0, -1.70), (-8.60, 0.0, -1.55)),
    ("Bone_Neck_02",  (-8.60, 0.0, -1.55), (-6.90, 0.0, -1.30)),
    ("Bone_Neck_03",  (-6.90, 0.0, -1.30), (-5.00, 0.0, -0.85)),
    ("Bone_Neck_04",  (-5.00, 0.0, -0.85), (-2.60, 0.0, -0.10)),
    ("Bone_Head",     (-2.60, 0.0, -0.10), (1.60, 0.0, -0.05)),
]

TAIL = [
    ("Bone_Tail_01", (-23.40, 0.0, -1.60), (-24.30, 0.0, -2.05)),
    ("Bone_Tail_02", (-24.30, 0.0, -2.05), (-25.10, 0.0, -2.80)),
    ("Bone_Tail_03", (-25.10, 0.0, -2.80), (-25.90, 0.0, -3.75)),
    ("Bone_Tail_04", (-25.90, 0.0, -3.75), (-26.60, 0.0, -4.85)),
    ("Bone_Tail_05", (-26.60, 0.0, -4.85), (-27.20, 0.0, -5.90)),
]

LIMBS = {
    # The z of the ankle joint is left as None: it is GROUND plus the scaled
    # height of whichever foot variation the limb uses, and `resolve_ankles()`
    # fills it in.
    #
    # The joints zigzag, and that is the point. A fore limb folds its elbow BACK
    # and its carpus forward; a hind limb throws its stifle FORWARD and its hock
    # back, which is the angular dog-leg every quadruped has and the clearest
    # single signal that a leg is a leg.
    #
    # The y values barely change down the chain. A limb that leaves the shoulder
    # at 2.3 and lands at 2.75 splays, and a splayed limb on a body this tall
    # reads as a trestle; the reference's drop almost vertically. They are also
    # further inboard than they were: at 2.30 the shoulder ring stood 0.8 units
    # proud of the flank and made a pillar stuck to the side of the animal.
    "FrontP": dict(parent="Bone_Spine_03", side=+1, foot="Broad3Toe",
                   joints=[(-11.20, 2.05, -0.60), (-11.75, 2.30, -5.30),
                           (-11.05, 2.42, -9.60), (-11.25, 2.45, None)],
                   toe=(-10.15, 2.45, -12.60)),
    "RearP":  dict(parent="Bone_Pelvis", side=+1, foot="Round4Toe",
                   joints=[(-21.20, 2.15, -0.90), (-20.40, 2.45, -5.10),
                           (-21.95, 2.60, -9.10), (-21.40, 2.62, None)],
                   toe=(-20.30, 2.62, -12.60)),
}
LIMB_SEGMENTS = ["Upper", "Lower", "Cannon"]

FOOT_SCALE = UNITS_PER_M * 0.95
FOOT_HEIGHT_M = {"Round4Toe": 0.34, "Broad3Toe": 0.30,
                 "Splayed5Toe": 0.26, "Cloven_Heavy": 0.36}
FOOT_SOCKET_M = {"Round4Toe": 0.225, "Broad3Toe": 0.250,
                 "Splayed5Toe": 0.215, "Cloven_Heavy": 0.230}
FOOT_SOLE_M = {"Round4Toe": 0.300, "Broad3Toe": 0.340,
               "Splayed5Toe": 0.360, "Cloven_Heavy": 0.280}

# Thick at the shoulder, tapering properly to the ankle -- but not *too*
# thick: at 2.20 the buried top ring plus its 1.08 condyle multiplier made a
# 4.7-unit sphere on a body only 7 wide, and the animal's shoulders and hips
# read as four beach balls before the legs even started.
LIMB_RADII = [1.75, 1.35, 1.05]
LIMB_BURY_UP = 1.80
LIMB_BURY_IN = 2.10
LIMB_INTO_FOOT = 0.55
SEG_PROFILE = [(0.00, 1.02), (0.35, 1.01), (0.70, 1.00), (0.95, 1.01)]
SEG_BOW = [0.035, -0.030, 0.020]
# One transition per leg, high up under the shoulder plates. Two
# transitions (plates, cream, teal) read as striped socks.
STOCKING_Z = -10.20

JAW = ((-0.90, 0.0, -0.55), (1.60, 0.0, -1.20))
# Under the middle of the trunk, midway between the shoulder (-11.8) and
# the hip (-21.6). vrescal_export.PIVOT must match: it is where Bone_Root
# sits, and the two disagreeing puts the root bone off the Unity origin.
PIVOT_X = -16.20


def resolve_ankles():
    for spec in LIMBS.values():
        h = FOOT_HEIGHT_M[spec["foot"]] * FOOT_SCALE
        x, y, _ = spec["joints"][-1]
        spec["joints"][-1] = (x, y, GROUND + h)


# --------------------------------------------------------------------------
# Body cross-sections
# --------------------------------------------------------------------------
#
# (x, half_width, top_z, bottom_z). Top and bottom are given separately rather
# than as centre-plus-radius because every interesting thing about this
# silhouette -- the humps, the flat belly line, the way the chest deepens
# without the back rising -- is a statement about one edge on its own.

BODY = [
    # The first two stations are *inside* the skull. The neck has to overlap the
    # sculpt, not stop against it: ending at the skull's rear face leaves a gap
    # wherever the two surfaces curve apart, and the head reads as floating.
    #
    # Neck: long, thick at the base, with a throat line that drops away under it.
    (-1.20, 0.88,  0.72, -0.82),
    (-2.40, 1.08,  1.02, -1.28),
    (-3.80, 1.32,  1.15, -2.05),
    (-5.20, 1.55,  1.05, -2.75),
    (-6.60, 1.82,  0.72, -3.30),
    (-8.00, 2.15,  0.28, -3.70),
    (-9.50, 2.55, -0.35, -3.95),   # base of neck
    (-10.30, 2.75, 0.30, -3.55),   # chest
    # Trunk. The shoulder and hip stations are the widest points on the animal,
    # and they are wide *here*, in the loft, not added later as muscle.
    #
    # The back line is a BACK, not a hump line: it runs almost level from the
    # withers to the croup. The humps are added by MUSCLES as two narrow blobs
    # sitting on it. That split matters -- raising `top` to make a hump inflates
    # the whole ellipse, so the hump comes out as wide as the animal and reads as
    # a beach ball. A camel's hump is roughly half the width of its ribcage, and
    # the only way to get that from a lofted section is to not put it in the loft.
    (-11.00, 3.45, 1.60, -3.90),   # withers
    (-12.20, 3.55, 1.95, -4.05),
    (-13.40, 3.50, 2.10, -4.15),
    (-14.30, 3.45, 2.15, -4.20),   # under hump 1
    (-15.40, 3.42, 2.10, -4.22),
    (-16.60, 3.40, 2.05, -4.25),   # saddle
    (-17.80, 3.45, 2.05, -4.20),
    (-18.90, 3.50, 2.00, -4.15),   # under hump 2
    (-20.10, 3.55, 1.85, -4.05),
    (-21.40, 3.55, 1.55, -3.85),   # hip
    (-22.60, 3.05, 0.60, -3.20),   # croup
    (-23.40, 2.45, -0.60, -2.60),  # rump
    # Tail: hangs. Each station drops further than the last.
    (-24.30, 1.40, -1.30, -2.85),
    (-25.10, 0.98, -2.15, -3.50),
    (-25.90, 0.68, -3.20, -4.35),
    (-26.60, 0.42, -4.35, -5.35),
    (-27.20, 0.20, -5.45, -6.35),
]

NECK_STATIONS = (0, 7)
TRUNK_STATIONS = (7, 18)
TAIL_STATIONS = (19, 23)
BELLY_LINE = -0.80


# --------------------------------------------------------------------------
# Anatomy
# --------------------------------------------------------------------------
#
# Muscle masses, port side; anything with a non-zero y is mirrored. These are
# the whole reason the animal reads as flesh rather than as tube. Strengths are
# in working units of outward displacement at the centre of the mass.

def MUSCLES():
    """Anatomical masses. Strengths are outward displacement at the centre.

    These are *definition*, not mass. The body's primary volumes -- shoulder,
    barrel, haunch -- are in the BODY cross-sections; a muscle here only says
    where that volume tightens or swells locally. Run them at the strength a
    primary mass would need (0.5-0.6 on a 2.5-unit radius) and each one
    inflates into a sphere stuck on the flank, which is what a 'ball on a
    stick' leg is made of.
    """
    b = S.Blob
    return [
        # forequarter
        b((-11.20, 2.75, -0.40), (2.60, 1.50, 2.60), 0.14),   # scapula
        b((-11.60, 2.90, -3.30), (1.70, 1.30, 2.20), 0.12),   # triceps
        b((-10.60, 1.70, -3.40), (1.90, 1.90, 1.40), 0.10),   # pectoral
        # barrel
        b((-13.40, 3.35, -2.40), (2.30, 1.10, 2.00), 0.10),   # rib 1
        b((-16.00, 3.35, -2.50), (2.30, 1.10, 2.00), 0.14),   # rib 2
        b((-15.20, 3.25, -0.60), (3.40, 1.20, 2.40), 0.14),   # latissimus
        # hindquarter
        b((-21.30, 2.85, -0.70), (2.50, 1.60, 2.40), 0.14),   # gluteal
        b((-20.85, 3.00, -3.30), (2.00, 1.40, 2.30), 0.12),   # quadriceps
        b((-21.50, 3.05, -7.20), (1.20, 0.95, 1.70), 0.15),   # gastrocnemius
        # midline -- not mirrored, y is already 0
        b((-11.00, 0.00, -3.90), (2.20, 1.60, 1.00), 0.14),   # sternum keel
        b((-7.60, 0.00, -3.30), (2.00, 1.25, 1.10), 0.14),    # throat
        b((-2.90, 0.00, -0.90), (1.30, 1.10, 0.85), 0.10),    # jowl
        b((-16.60, 0.00, -4.30), (4.80, 2.40, 1.00), 0.12),   # belly
        b((-7.20, 0.00, 0.10), (3.00, 1.15, 1.00), 0.12),     # nuchal crest
        # The two humps, as narrow masses rather than a wider body. Radii
        # are half the trunk's, which is the proportion a camel's hump has
        # to its ribcage.
        b((-14.00, 0.00, 2.10), (3.10, 2.05, 3.50), 1.72, one_sided=False),
        b((-18.60, 0.00, 2.00), (2.70, 1.85, 3.10), 1.34, one_sided=False),
    ]


def FOLDS():
    b, r = S.Blob, S.Ring
    return [
        # Where a limb leaves the body the skin gathers into a deep vertical
        # crease. Without these the leg looks pushed into the flank like a peg.
        b((-10.55, 2.95, -2.60), (0.65, 1.50, 2.60), -0.28),
        b((-20.30, 2.95, -2.70), (0.65, 1.50, 2.60), -0.26),
        b((-11.50, 2.85, -5.55), (0.50, 1.15, 0.60), -0.16),   # elbow crease
        b((-20.60, 3.00, -5.35), (0.50, 1.15, 0.60), -0.16),   # stifle crease
        # Neck rings. A blob would dimple the middle of the neck; a wrinkle is
        # a gather *around* it, which is what Ring exists for. The axis follows
        # the neck's own run, roughly (1, 0, 0.2).
        r((-2.55, 0.0, -0.45), (1.0, 0.0, 0.20), 1.05, 0.35, -0.11),
        r((-4.40, 0.0, -0.72), (1.0, 0.0, 0.20), 1.20, 0.38, -0.13),
        r((-6.40, 0.0, -1.15), (1.0, 0.0, 0.20), 1.42, 0.42, -0.14),
        r((-8.60, 0.0, -1.55), (1.0, 0.0, 0.20), 1.72, 0.46, -0.13),
    ]


SKIN_NOISE_SCALE = 0.62
SKIN_NOISE_AMOUNT = 0.072

# Armour. Two passes: a fine dense field over the body, then a coarser, taller
# one along the dorsal ridge and tail where the reference's scutes become
# pointed overlapping shields rather than flat mosaic.
# Plate height is 0.09 units -- 2.5 cm on a 4.6 m animal. The first pass used
# 0.20 with 35 % jitter and the body came out looking like a shingled roof:
# armour reads from the *crack pattern*, not from how far the plates stand off
# the hide, and anything that catches its own shadow that hard stops looking
# like part of the animal.
MOSAIC_BODY = dict(count=340, gap=0.085, height=0.090, height_jitter=0.22)
MOSAIC_RIDGE = dict(count=58, gap=0.105, height=0.26, height_jitter=0.30)


def _section_at(x):
    """Interpolated (half_width, top_z, bottom_z) of the body at this x."""
    if x >= BODY[0][0]:
        return BODY[0][1:]
    if x <= BODY[-1][0]:
        return BODY[-1][1:]
    for a, c in zip(BODY, BODY[1:]):
        if c[0] <= x <= a[0]:
            f = (a[0] - x) / max(a[0] - c[0], 1e-6)
            return tuple(a[i] + (c[i] - a[i]) * f for i in (1, 2, 3))
    return BODY[-1][1:]


def armour_limit(p, above=None):
    """Is this point on the armoured part of the animal?

    Measured against the *local* cross-section rather than a world height, so
    the boundary follows the belly line up over the chest and down the tail
    instead of cutting a straight waterline across a body that is 13 units deep
    at the ribs and 2 at the tail.

    The thresholds differ by region because the reference animal's do: the
    trunk is plated to just below its widest point, the neck only along its
    crest, and the tail almost all the way round. Plating everything to one
    height wrapped the armour under the belly, which reads as a woodlouse -- the
    bare underside is what says this thing has something soft to protect.
    """
    if p.x > -1.60 or p.x < -27.6:
        return False
    if above is None:
        if p.x > -9.50:            # neck: crest only
            above = 0.52
        elif p.x > -22.50:         # trunk: well down the flank
            above = -0.22
        else:                      # tail: nearly all round
            above = -0.42
    _ry, top, bot = _section_at(p.x)
    centre = (top + bot) * 0.5
    rz = max((top - bot) * 0.5, 1e-6)
    # Ragged, not ruled. A clean threshold draws a hard horizontal line down the
    # flank where the plates stop, and on a two-tone animal that line reads as a
    # painted racing stripe rather than as the edge of a scale field. Perturbing
    # it with the same turbulence the skin uses breaks it into an interlocking
    # edge, which is what the boundary looks like on anything that grew.
    edge = noise.turbulence(p * 0.42, 2, False) * 0.30
    return (p.z - centre) / rz > above + edge


def limb_armour_limit(p):
    """Plates on the upper limbs, which the reference has and the body pass
    misses because the legs are separate geometry outboard of the trunk."""
    if p.z < -10.40 or p.z > -2.0:
        return False
    for spec in LIMBS.values():
        jx = spec["joints"][0][0]
        if abs(p.x - jx) < 3.2 and abs(p.y) > 1.4:
            return True
    return False


def ridge_limit(p):
    """The dorsal crest and the tail -- where the scutes stand up."""
    if p.x > -3.0 or p.x < -27.6:
        return False
    if p.x < -22.50:
        return armour_limit(p, above=-0.30)
    return armour_limit(p, above=0.66)


# --------------------------------------------------------------------------
# Arc-length weighting along a bone chain
# --------------------------------------------------------------------------

class Chain:
    """A bone chain that can answer 'what weights belong at this point?'."""

    def __init__(self, bones):
        self.names = [b[0] for b in bones]
        self.heads = [Vector(b[-2]) for b in bones]
        self.tails = [Vector(b[-1]) for b in bones]
        self.starts = []
        s = 0.0
        for h, t in zip(self.heads, self.tails):
            self.starts.append(s)
            s += (t - h).length
        self.total = s

    def arc_of(self, p):
        best, best_d = 0.0, 1e30
        for i, (h, t) in enumerate(zip(self.heads, self.tails)):
            d = t - h
            L = d.length
            if L < 1e-9:
                continue
            u = max(0.0, min(1.0, (Vector(p) - h).dot(d) / (L * L)))
            q = h + d * u
            dist = (Vector(p) - q).length
            if dist < best_d:
                best_d, best = dist, self.starts[i] + u * L
        return best

    def weights(self, p, extra=None):
        """Weights for a point, at most two bones deep."""
        s = self.arc_of(p)
        w = {}
        for i, name in enumerate(self.names):
            lo = self.starts[i]
            hi = lo + (self.tails[i] - self.heads[i]).length
            if lo - BLEND <= s <= hi + BLEND:
                if s < lo:
                    v = 0.5 * (1.0 - (lo - s) / BLEND)
                elif s > hi:
                    v = 0.5 * (1.0 - (s - hi) / BLEND)
                else:
                    v = 1.0
                    if s - lo < BLEND:
                        v = 0.5 + 0.5 * (s - lo) / BLEND
                    if hi - s < BLEND:
                        v = min(v, 0.5 + 0.5 * (hi - s) / BLEND)
                if v > 0.0:
                    w[name] = max(w.get(name, 0.0), v)
        if not w:
            w = {self.names[0]: 1.0}
        if extra:
            for k, v in extra.items():
                w[k] = w.get(k, 0.0) + v
        return w


def frame_at(points, i):
    """Right and up vectors for station `i` of a centreline polyline."""
    if i == 0:
        tan = (points[1] - points[0])
    elif i == len(points) - 1:
        tan = (points[-1] - points[-2])
    else:
        tan = (points[i + 1] - points[i - 1])
    tan = tan.normalized()
    right = tan.cross(Vector((0.0, 0.0, 1.0)))
    if right.length < 1e-6:
        right = Vector((0.0, 1.0, 0.0))
    right.normalize()
    up = right.cross(tan).normalized()
    return right, up


def ring_at(ry, top, bot, n=RING, flat=0.0, crest=0.0):
    """Ring points as (lateral, absolute z, w) triples.

    `w` is +1 at the top of the ring and -1 underneath; the caller uses it to
    decide belly material.
    """
    cz = (top + bot) * 0.5
    rz = (top - bot) * 0.5
    out = []
    for i in range(n):
        a = 2.0 * math.pi * i / n
        w = math.cos(a)
        u = ry * math.sin(a)
        v = rz * w
        if crest and w > 0.0:
            v += crest * (w ** 3)
        if flat and w < 0.0:
            v += flat * (w * w) * rz
        out.append((u, cz + v, w))
    return out


# --------------------------------------------------------------------------
# Base loft
# --------------------------------------------------------------------------

def build_body(part, spine_chain, tail_chain):
    centres = [Vector((x, 0.0, (top + bot) * 0.5)) for x, _, top, bot in BODY]
    rows = []
    for i, (x, ry, top, bot) in enumerate(BODY):
        right, up = frame_at(centres, i)
        c = centres[i]
        flat = 0.34 if TRUNK_STATIONS[0] <= i <= TRUNK_STATIONS[1] else 0.0
        crest = 0.26 if 8 <= i <= 16 else 0.0
        chain = tail_chain if i >= TAIL_STATIONS[0] else spine_chain
        wts = chain.weights(c)
        row = []
        for (u, z, w) in ring_at(ry, top, bot, flat=flat, crest=crest):
            p = c + right * u + up * (z - c.z)
            row.append(part.vert(p, wts))
        rows.append(row)

    for a, b in zip(rows, rows[1:]):
        for i in range(RING):
            j = (i + 1) % RING
            w = math.cos(2.0 * math.pi * i / RING)
            part.face((a[i], a[j], b[j], b[i]),
                      BELLY if w < BELLY_LINE else HIDE)

    tip = part.vert(centres[-1] + Vector((-0.45, 0.0, -0.55)),
                    tail_chain.weights(centres[-1]))
    for i in range(RING):
        part.face((rows[-1][i], rows[-1][(i + 1) % RING], tip), HIDE)


def limb_bones(name, spec):
    side = spec["side"]
    j = [Vector((x, y * side, z)) for (x, y, z) in spec["joints"]]
    toe = Vector((spec["toe"][0], spec["toe"][1] * side, spec["toe"][2]))
    out = [("Bone_%s_%s" % (name, seg), j[k], j[k + 1])
           for k, seg in enumerate(LIMB_SEGMENTS)]
    out.append(("Bone_%s_Foot" % name, j[-1], toe))
    return out


def build_limb(part, name, spec):
    """One columnar leg, lofted down its joint chain.

    The top ring is pushed up *and inboard* so it hides inside the trunk. The
    joint condyles and muscle bellies that make it read as a limb rather than a
    strut are added later by the MUSCLES pass -- the loft only carries the
    shaft.
    """
    side = spec["side"]
    joints = [Vector((x, y * side, z)) for (x, y, z) in spec["joints"]]
    up_dir = (joints[0] - joints[1]).normalized()
    inboard = Vector((0.0, -side, 0.0))
    buried = joints[0] + up_dir * LIMB_BURY_UP + inboard * LIMB_BURY_IN

    radii = LIMB_RADII + [FOOT_SOCKET_M[spec["foot"]] * FOOT_SCALE * 1.02]

    stations = [(buried, radii[0] * 1.05)]
    for s in range(len(joints) - 1):
        a, b = joints[s], joints[s + 1]
        ra, rb = radii[s], radii[s + 1]
        seg = b - a
        perp = seg.cross(Vector((0.0, 1.0, 0.0)))
        perp = perp.normalized() if perp.length > 1e-6 else Vector((1.0, 0, 0))
        for t, mul in SEG_PROFILE:
            c = a + seg * t + perp * (SEG_BOW[s] * seg.length
                                      * math.sin(math.pi * t))
            stations.append((c, (ra + (rb - ra) * t) * mul))
    stations.append((joints[-1], radii[-1]))
    down = (joints[-1] - joints[-2]).normalized()
    stations.append((joints[-1] + down * LIMB_INTO_FOOT, radii[-1] * 0.86))

    chain = Chain(limb_bones(name, spec))
    centres = [c for c, _ in stations]

    rows = []
    for i, (c, r) in enumerate(stations):
        right, up = frame_at(centres, i)
        extra = ({spec["parent"]: 1.4} if i == 0
                 else {spec["parent"]: 0.45} if i == 1 else None)
        wts = chain.weights(c, extra)
        row = []
        for k in range(RING):
            a = 2.0 * math.pi * k / RING
            p = c + right * (r * 0.94 * math.sin(a)) + up * (r * math.cos(a))
            row.append(part.vert(p, wts))
        rows.append(row)

    for (a, ca), (b, cb) in zip(zip(rows, centres), zip(rows[1:], centres[1:])):
        band = BELLY if (ca.z + cb.z) * 0.5 < STOCKING_Z else HIDE
        for i in range(RING):
            j = (i + 1) % RING
            part.face((a[i], a[j], b[j], b[i]), band)

    return joints[-1]


# --------------------------------------------------------------------------
# Components
# --------------------------------------------------------------------------

def load_component(path, names):
    before = set(bpy.data.meshes.keys())
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        missing = [n for n in names if n not in set(src.meshes)]
        if missing:
            raise SystemExit("%s has no mesh %s" % (path, missing))
        dst.meshes = list(names)
    out = {}
    for name in names:
        me = bpy.data.meshes[name]
        out[name] = ([Vector(v.co) for v in me.vertices],
                     [(list(p.vertices), p.material_index)
                      for p in me.polygons])
    for k in set(bpy.data.meshes.keys()) - before:
        bpy.data.meshes.remove(bpy.data.meshes[k])
    return out


def stamp(part, geom, matrix, weights, mat_map):
    verts, faces = geom
    base = len(part.co)
    for v in verts:
        part.vert(matrix @ v, weights)
    for idx, mi in faces:
        part.face([base + i for i in idx], mat_map.get(mi, 0), True)


# --------------------------------------------------------------------------
# Armature
# --------------------------------------------------------------------------

def all_bones():
    out = [("Bone_Root", None, (PIVOT_X, 0.0, GROUND),
            (PIVOT_X + 2.6, 0.0, GROUND))]
    prev = "Bone_Root"
    for name, head, tail in SPINE:
        out.append((name, prev, head, tail))
        prev = name
    out.append(("Bone_Jaw", "Bone_Head", JAW[0], JAW[1]))
    prev = "Bone_Pelvis"
    for name, head, tail in TAIL:
        out.append((name, prev, head, tail))
        prev = name
    for base, spec in LIMBS.items():
        for side, tag in ((+1, "P"), (-1, "S")):
            s = dict(spec)
            s["side"] = side
            name = base[:-1] + tag
            parent = spec["parent"]
            for bname, head, tail in limb_bones(name, s):
                out.append((bname, parent, tuple(head), tuple(tail)))
                parent = bname
    return out


def build_armature(bones):
    arm_data = bpy.data.armatures.new("Arm_Vrescal")
    arm = bpy.data.objects.new("Arm_Vrescal", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    for name, parent, head, tail in bones:
        eb = arm_data.edit_bones.new(name)
        eb.head, eb.tail, eb.roll = Vector(head), Vector(tail), 0.0
        eb.use_deform = True
        if parent:
            eb.parent = arm_data.edit_bones[parent]
            eb.use_connect = (eb.parent.tail - eb.head).length < 1e-5
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


def reparent_to_bone(obj, arm, bone_name):
    """Bone-parent `obj` without moving it.

    The parent inverse is computed rather than assigned through
    `obj.matrix_world = ...`: writing the world matrix back relies on the
    dependency graph having evaluated a parent relationship set two lines
    earlier on an armature created in the same script, and when it has not, the
    write silently lands in the wrong space. `parent_type='BONE'` also hangs the
    child off the bone's *tail*, hence the translation along local Y.
    """
    mw = obj.matrix_world.copy()
    bone = arm.data.bones[bone_name]
    parent_world = (arm.matrix_world @ bone.matrix_local
                    @ Matrix.Translation((0.0, bone.length, 0.0)))
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    obj.matrix_parent_inverse = parent_world.inverted()
    obj.matrix_basis = mw


# --------------------------------------------------------------------------
# Assembly
# --------------------------------------------------------------------------

def guard():
    names = set(bpy.data.objects.keys())
    if "Mesh_Vrescal_Body" in names:
        raise SystemExit(
            "This file has already been rebuilt (Mesh_Vrescal_Body exists).\n"
            "The .blend is the source of truth -- edit it in Blender rather "
            "than re-running this. Restore from _backups~ to start over.")
    if "Mesh_Vrescal_Plate_01" not in names:
        raise SystemExit("Not the pre-rebuild Vrescal: no Mesh_Vrescal_Plate_01.")
    for k in KEEP:
        if k not in names:
            raise SystemExit("Missing the sculpt piece %s -- refusing to run." % k)


def purge():
    """Delete everything the rebuild replaces, keeping the author's sculpt.

    Clearing `parent` does not keep the world transform -- the parent inverse
    goes with it and the object jumps by the whole of its old parent's matrix.
    The sculpt is bone-parented to the armature about to be deleted, so the
    world matrix is captured first and put back, or the head ends up a metre and
    a half off the centreline with nothing to say so.
    """
    for o in list(bpy.data.objects):
        if o.name in KEEP:
            mw = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = mw
            continue
        bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)


def retone_sculpt(mats):
    """Point the kept sculpt's hide slots at the body's hide material.

    The head is not modified -- not one vertex moves -- but it cannot keep
    `Mat_Hide_Sand_Pale` while the body it sits on is `Mat_Hide_Bone_Cream`.
    Sand_Pale is a saturated orange-yellow; against the desaturated body it
    reads as a different animal's head grafted on, which is exactly how the
    first pass looked. This is a slot remap, nothing more.
    """
    swap = {"Mat_Hide_Sand_Pale": mats[HIDE],
            "Mat_Hide_Plate_Tan": mats[PLATE]}
    for name in KEEP:
        me = bpy.data.objects[name].data
        for i, m in enumerate(me.materials):
            if m is not None and m.name in swap:
                me.materials[i] = swap[m.name]


def main():
    guard()
    purge()
    resolve_ankles()

    mats = B.link_materials(MATS)
    retone_sculpt(mats)
    bones = all_bones()
    group_order = [b[0] for b in bones]

    spine_chain = Chain([(n, h, t) for n, h, t in SPINE])
    tail_chain = Chain([("Bone_Pelvis",) + tuple(SPINE[0][1:])] +
                       [(n, h, t) for n, h, t in TAIL])

    # -- base loft ---------------------------------------------------------
    part = B.SkinPart(mats)
    build_body(part, spine_chain, tail_chain)
    ankles = {}
    for base, spec in LIMBS.items():
        for side, tag in ((+1, "P"), (-1, "S")):
            s = dict(spec)
            s["side"] = side
            name = base[:-1] + tag
            ankles[name] = (build_limb(part, name, s), s)

    # Feet come in as component geometry, rigid to the foot bone: a foot is
    # bone, not muscle, and weighting it into the cannon would let the sole flex.
    foot_geom = load_component(
        FEET_BLEND, sorted({"Mesh_Pad_%s" % s["foot"] for s in LIMBS.values()}))
    for name, (ankle, spec) in ankles.items():
        m = (Matrix.Translation(ankle)
             @ Matrix.Diagonal((FOOT_SCALE, FOOT_SCALE, FOOT_SCALE, 1.0)))
        stamp(part, foot_geom["Mesh_Pad_%s" % spec["foot"]], m,
              {"Bone_%s_Foot" % name: 1.0}, FOOT_MAT_MAP)

    body = part.finish("Mesh_Vrescal_Body", bpy.context.scene.collection,
                       group_order)

    # -- subdivide, then treat the surface ---------------------------------
    mod = body.modifiers.new("Subsurf", 'SUBSURF')
    mod.levels = mod.render_levels = SUBSURF
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier=mod.name)

    bm = bmesh.new()
    bm.from_mesh(body.data)

    S.displace(bm, MUSCLES())
    S.displace(bm, FOLDS())
    S.skin_noise(bm, SKIN_NOISE_SCALE, SKIN_NOISE_AMOUNT, octaves=3)

    # The armour mosaic is deliberately not run. `vrescal_surface.plate_mosaic`
    # still exists and works, but a scale field over a form that is not yet
    # right hides the form rather than decorating it -- every judgement about
    # the silhouette got harder to make with 400 plates on top of it. Shape
    # first. Turning it back on is one call, once the body underneath is
    # something worth armouring.

    for f in bm.faces:
        f.smooth = True
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()

    # -- rig ---------------------------------------------------------------
    arm = build_armature(bones)
    body.parent = arm
    body.modifiers.new("Armature", 'ARMATURE').object = arm

    for name, bone in (("Mesh_Vrescal_Skull", "Bone_Head"),
                       ("Mesh_Vrescal_EyeP", "Bone_Head"),
                       ("Mesh_Vrescal_EyeS", "Bone_Head"),
                       ("Mesh_Vrescal_Jaw", "Bone_Jaw")):
        reparent_to_bone(bpy.data.objects[name], arm, bone)

    # Dimensions read off the tables rather than repeated as literals -- they
    # have now been restated three times and drifted from the geometry twice.
    # Measured off the finished mesh, not off the tables. The humps are added
    # by MUSCLES now, so BODY's `top` no longer knows how tall the animal is.
    zs = [v.co.z for v in body.data.vertices]
    xs = [v.co.x for v in body.data.vertices]
    belly = min(b[3] for b in BODY[8:19])

    tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
    print("Vrescal rebuilt: %d bones, %d verts, %d tris"
          % (len(bones), len(body.data.vertices), tris))
    print("  height %.2f m, length %.2f m, belly clearance %.2f m"
          % ((max(zs) - GROUND) / UNITS_PER_M,
             (2.51 - min(xs)) / UNITS_PER_M,
             (belly - GROUND) / UNITS_PER_M))

    bpy.ops.wm.save_mainfile()
    print("Saved %s" % bpy.data.filepath)


# Guarded: `vrescal_anim.py` imports this module for the geometry constants, and
# an unguarded call would run a destructive rebuild as a side effect of import.
if __name__ == "__main__":
    main()
