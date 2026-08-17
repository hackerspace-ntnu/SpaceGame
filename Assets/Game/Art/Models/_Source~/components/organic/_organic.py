"""Geometry helpers shared by the library's organic components.

`_buildlib` knows how to make bevelled boxes, capped cylinders and lofted hulls
-- everything a machine is made of. Creatures need a different vocabulary: a
limb is a tapering tube whose cross-section is an ellipse squashed differently
at every station, with a muscle bulge at one end and a joint condyle at the
other, and whose centreline bows rather than running straight.

Rather than each creature script carrying its own copy of "make a ring of
twelve points and loft it", that lives here.

## The conventions every organic component in this library follows

**Limb segments** are built along **+X**, with the *proximal* joint (the end
nearer the body) at the origin and the distal joint at +X. Dorsal -- the top of
the limb when the animal stands -- is **+Z**. Segments are kept bilaterally
symmetric about their own local y = 0 so the same mesh serves the left and
right sides without mirroring.

**Feet** are built with the ankle/wrist socket at the origin, the sole below it
at -Z, and the toes pointing **+X**. Also y-symmetric.

**Claws** are built with the base -- where the claw emerges from the toe -- at
the origin, growing along **+X** and curving down toward -Z.

**Haunches** are built for the **+Y (port) side** with the limb socket at the
origin and the muscle mass reaching inboard toward -Y. Use `_buildlib.mirror_y`
for the starboard copy.

Author every component at final real-world size. A model that works at a
different scale should scale the mesh data on the way in (see `_buildlib.SCALE`)
rather than leaving a non-unit scale on the object.
"""

import math
import os
import sys

from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))

RING_SEGMENTS = 12

# Toe fan geometry per foot variation, shared between the foot component that
# builds the toes and any model that needs to know where the tips ended up --
# a model attaching separate claws, for one. Keeping it here rather than as
# literals inside `foot_splayed.py` is what stops the two drifting apart and
# leaving claws floating a centimetre off the toe.
#
#   (count, spread, ring_x, ring_y, base_z, length, radius, joints)
TOE_FANS = {
    "Manus4": (4, 0.60, 0.130, 0.132, -0.046, 0.125, 0.042, 3),
    "Pes5": (5, 0.76, 0.146, 0.150, -0.052, 0.150, 0.044, 3),
    "Fringed": (4, 0.64, 0.126, 0.130, -0.044, 0.122, 0.038, 3),
}


def fan_tips(name):
    """Where each toe of the named foot variation ends, in the foot's local
    space. Mirrors the stepping `toe`/`fan` do when they build the capsules."""
    count, spread, rx, ry, z, length, _radius, joints = TOE_FANS[name]
    seg = length / joints
    drop = sum(seg * 0.10 * j for j in range(joints))
    tips = []
    for i in range(count):
        t = 0.0 if count == 1 else (i / (count - 1.0)) * 2.0 - 1.0
        a = t * spread
        tips.append((Vector((rx * math.cos(a), ry * math.sin(a), z))
                     + Vector((math.cos(a), math.sin(a), 0.0)) * length
                     - Vector((0.0, 0.0, drop)), a))
    return tips


def ring(ry, rz, n=RING_SEGMENTS, cy=0.0, cz=0.0, ridge=0.0, keel=0.0,
         flat_bottom=0.0):
    """A closed cross-section profile in the (u=y, v=z) plane.

    `ry`/`rz` are the semi-axes. The extras are what stop a limb reading as a
    length of pipe:

      `ridge`       pushes the topmost points further up -- a dorsal crest.
      `keel`        pushes the bottom points further down.
      `flat_bottom` lifts the bottom toward the centre, flattening the underside
                    the way a weight-bearing limb or a sole actually is.

    Points start at the top (+Z) and run round through +Y. Every profile in one
    loft must have the same point count, which is why `n` is rarely overridden.
    """
    pts = []
    for i in range(n):
        a = 2.0 * math.pi * i / n
        w = math.cos(a)                       # +1 at the top, -1 underneath
        u = ry * math.sin(a)
        v = rz * w
        if ridge and w > 0.0:
            v += ridge * (w ** 3)
        if keel and w < 0.0:
            v -= keel * (abs(w) ** 3)
        if flat_bottom and w < 0.0:
            v += flat_bottom * (abs(w) ** 2) * rz
        pts.append((cy + u, cz + v))
    return pts


def bone(stations, n=RING_SEGMENTS):
    """Loft sections for a limb segment.

    `stations` is a list of dicts, each `{x, ry, rz, ...}` where the extra keys
    are `ring`'s. Written as dicts because a station with six positional floats
    is unreadable at the call site and this is the most-edited data in any
    creature script.
    """
    out = []
    for st in stations:
        st = dict(st)
        x = st.pop("x")
        out.append((x, ring(n=n, **st)))
    return out


def shaped(points, bow=0.0, droop=0.0, **common):
    """Stations from an explicit `[(x, ry, rz), ...]` list.

    A fourth element on any point overrides the centreline height at that
    station, for shapes whose curve is not a simple arc. `droop` is the common
    case that is not an arc either: it falls as t squared, which is the shape of
    a claw or a horn -- almost straight where it leaves the toe, hooking hardest
    at the tip.

    `arc_stations` interpolates a few radii evenly, which is fine for a taper
    but cannot place a muscle belly at 20% and a joint flare at 90%. This takes
    the stations verbatim, which is what a limb actually needs: start and end
    each with a near-zero ring so the loft caps round over instead of stopping
    dead on a flat disc.
    """
    span = points[-1][0] - points[0][0]
    base_cz = common.pop("cz", 0.0)
    out = []
    for pt in points:
        x, ry, rz = pt[0], pt[1], pt[2]
        st = dict(common)
        t = (x - points[0][0]) / span if span else 0.0
        # `bow` adds to any baseline cz rather than replacing it, so a caller can
        # sit the whole run below the origin (a foot pad hanging under its ankle)
        # and still bow it.
        cz = base_cz + bow * math.sin(math.pi * t) - droop * t * t
        st.update(x=x, ry=ry, rz=rz, cz=pt[3] if len(pt) > 3 else cz)
        out.append(st)
    return out


def rounded(points, cap=0.22):
    """Bracket a station list with two collapsed rings so the ends dome over.

    `cap` is the fraction of the end radius the terminal ring keeps -- not zero,
    because a zero-radius ring collapses to coincident vertices that
    `remove_doubles` turns into a pole with a pinched normal.
    """
    first, last = points[0], points[-1]
    lead = (first[0] - first[2] * 0.55, first[1] * cap, first[2] * cap)
    tail = (last[0] + last[2] * 0.55, last[1] * cap, last[2] * cap)
    return [lead] + list(points) + [tail]


def arc_stations(length, count, radii, bow=0.0, twist=0.0, **common):
    """Stations evenly spaced along +X, with the centreline bowed in Z.

    `radii` is a list of (ry, rz) sampled at equal steps and linearly
    interpolated to `count` stations, so a caller gives the three or four radii
    that matter rather than one per station. `bow` lifts the middle of the
    segment -- positive arcs the limb upward, which is what makes a leg read as
    sprung rather than as a straight strut.
    """
    stations = []
    for i in range(count):
        t = i / (count - 1.0)
        pos = t * (len(radii) - 1)
        lo = min(int(pos), len(radii) - 2)
        f = pos - lo
        ry = radii[lo][0] * (1 - f) + radii[lo + 1][0] * f
        rz = radii[lo][1] * (1 - f) + radii[lo + 1][1] * f
        st = dict(common)
        st.update(x=t * length, ry=ry, rz=rz,
                  cz=bow * math.sin(math.pi * t))
        if twist:
            st["cy"] = twist * math.sin(math.pi * t)
        stations.append(st)
    return stations


def taper_to_point(sections, tip, n=RING_SEGMENTS):
    """Close a loft by collapsing the last ring toward a single point.

    Used for claw tips and toe ends, where a flat cap reads as a cut-off tube.
    Returns the sections with one extra near-degenerate station appended --
    near, not exactly, because a zero-radius ring makes twelve coincident
    vertices that `remove_doubles` then merges into a pole with bad normals.
    """
    x, ry, rz = tip
    return list(sections) + [(x, ring(ry, rz, n=n))]


def scutes(part, start, end, count, size, mat, rise=0.55, taper=0.6):
    """A row of overlapping keratin plates along a line -- the armour read.

    Each plate is a wedge box tilted to lean back over the one behind it, and
    they shrink toward the far end so the row reads as following a taper.
    """
    from mathutils import Matrix
    start, end = Vector(start), Vector(end)
    faces = []
    for i in range(count):
        t = i / max(count - 1.0, 1.0)
        p = start.lerp(end, t)
        s = size * (1.0 - taper * t)
        rot = Matrix.Rotation(math.radians(-28.0), 4, 'Y')
        faces += part.box((p.x, p.y, p.z + s * rise * 0.5),
                          (s * 1.5, s * 1.9, s), mat, rot=rot)
    return faces


def digit(part, base, direction, length, radius, mat, joints=3, spread=0.0):
    """One toe: a short chain of shrinking capsule segments.

    `direction` is the horizontal heading in radians, 0 being straight ahead
    along +X. Built as separate overlapping segments rather than one lofted tube
    because the overlaps read as knuckles for free.
    """
    base = Vector(base)
    d = Vector((math.cos(direction + spread), math.sin(direction + spread), 0.0))
    faces = []
    p = base.copy()
    for j in range(joints):
        f = 1.0 - 0.22 * j
        seg = length / joints
        centre = p + d * (seg * 0.5) - Vector((0, 0, seg * 0.06 * j))
        faces += part.cyl(centre, radius * f, seg * 1.12, axis='Z', seg=8,
                          mat=mat, radius_top=radius * f * 0.86,
                          rot=_heading(d))
        p = p + d * seg - Vector((0, 0, seg * 0.12 * j))
    return faces, p, d


def heading_matrix(d):
    """Rotation whose local +Z lies along `d`.

    `Part.cyl(..., axis='Z', rot=m)` builds along +Z and then applies `m`, so
    this is what lets one call place a segment pointing anywhere -- a toe fanned
    out across the ground plane, or a limb bone raked back under the body.
    """
    from mathutils import Matrix
    z = Vector(d).normalized()
    up = Vector((0, 0, 1))
    if abs(z.dot(up)) > 0.98:
        up = Vector((1, 0, 0))
    x = up.cross(z).normalized()
    y = z.cross(x).normalized()
    return Matrix((x, y, z)).transposed().to_4x4()


_heading = heading_matrix
