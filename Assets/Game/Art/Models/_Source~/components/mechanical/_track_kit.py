"""Shared kit for the crawler track system: `track_link`, `track_wheel` and
`track_bogie`.

Two things live here rather than in each component script.

**The track geometry, once.** A sprocket only drives a track if its teeth land
in the gaps between the links, and that is decided by the link's pitch, its
hinge depth and the length of its plate. Those numbers are defined here, and
the sprocket derives its pitch radius and tooth width from them, so changing
the link cannot silently leave the sprocket out of mesh with it.

**Dropping faces nobody can see.** The link, bolt and cleat budgets are tens of
triangles, so the face that is buried in whatever the detail sits on is
deleted rather than paid for. `drop()` is how every script here does that.

Importable, like `_console_kit.py`; not a generator itself.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))

import bmesh  # noqa: E402
from mathutils import Vector  # noqa: E402

# --------------------------------------------------------------------------
# Link geometry. Link-local frame: origin at the centre of the INNER face (the
# face the wheels roll on), +Z towards the wheels, -Z towards the ground, -Y
# along the direction of travel of the lower run, X across the track.
# --------------------------------------------------------------------------

PITCH = 0.36                 # hinge-to-hinge distance along Y
LINK_WIDTH = 1.20            # across the track, X
PLATE_T = 0.10               # plate thickness, inner face at z = 0
KNUCKLE_R = 0.05
HINGE_Y = PITCH / 2.0        # hinge axes at y = +-0.18 ...
HINGE_Z = -PLATE_T / 2.0     # ... and half a plate below the inner face
PLATE_HALF_Y = 0.145         # plate stops short of the hinge; knuckles bridge it
# Each end carries one knuckle over half the width, the +Y end on the -X half
# and the -Y end on the +X half, so two neighbouring links' knuckles share one
# hinge axis without overlapping. Beyond |x| = KNUCKLE_OUT the joint is an open
# gap: that is where the sprocket teeth go.
KNUCKLE_IN, KNUCKLE_OUT = 0.02, 0.30

# --------------------------------------------------------------------------
# Sprocket geometry, derived from the link.
# --------------------------------------------------------------------------

SPROCKET_TEETH = 13
# The hinge axes of a wrapped track lie on this circle (the pitch circle).
PITCH_R = PITCH / (2.0 * math.sin(math.pi / SPROCKET_TEETH))
# Where a wrapped link's origin (inner-face centre) sits: the chord's midpoint
# pulled in by the hinge depth.
LINK_SEAT_R = PITCH_R * math.cos(math.pi / SPROCKET_TEETH) + HINGE_Z
# Tooth rings sit inside the knuckle-free outer band of the link.
SPROCKET_RING_X = 0.45

# Anything that sits on a surface is buried this far into it, so the hidden
# face is inside and nothing is coplanar.
EMBED = 0.005


def drop(p, faces, direction):
    """Delete the faces among `faces` whose normal points along `direction`.

    Used for the face of a detail that is buried in the part it sits on — the
    base of a bolt, the top of a cleat inside the plate. Returns what is left.
    """
    d = Vector(direction).normalized()
    gone = [f for f in faces if f.is_valid and f.normal.normalized().dot(d) > 0.9]
    bmesh.ops.delete(p.bm, geom=gone, context='FACES_ONLY')
    return [f for f in faces if f.is_valid]


def bolt(p, centre, axis, sign, mat, radius=0.03, height=0.03, seg=6):
    """A bolt head standing out of a surface along `sign` * `axis`.

    Its base is buried EMBED into the surface and deleted, since nothing can
    ever see it.
    """
    c = Vector(centre)
    n = Vector({'X': (1, 0, 0), 'Y': (0, 1, 0), 'Z': (0, 0, 1)}[axis]) * sign
    faces = p.cyl(c + n * (height / 2.0 - EMBED), radius, height, axis, seg,
                  mat, radius_top=radius * 0.8)
    return drop(p, faces, -n)


def bolt_circle(p, centre, axis, sign, ring_r, count, mat, phase=0.0, **kw):
    """`count` bolts on a circle of radius `ring_r` about `axis`."""
    c = Vector(centre)
    faces = []
    for i in range(count):
        a = phase + 2.0 * math.pi * i / count
        u, v = math.cos(a) * ring_r, math.sin(a) * ring_r
        off = {'X': (0, u, v), 'Y': (u, 0, v), 'Z': (u, v, 0)}[axis]
        faces += bolt(p, c + Vector(off), axis, sign, mat, **kw)
    return faces
