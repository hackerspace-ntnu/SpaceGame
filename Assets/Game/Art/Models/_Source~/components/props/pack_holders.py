"""components/props/pack_holders — the five holders the deployed rig grips gear with.

Companion to `expedition_rig`. Spec:
docs/superpowers/specs/2026-08-23-physical-inventory-design.md, section 3.5.

The pack has no pockets. Every item on it is held by a strap, a loop or a bungee
and is therefore always visible, and each holder is shaped by what it grips — so
you can see the shape of what is *missing*. `HolderBuilder` picks one of these by
the item's measured shape and stretches it onto that item's box.

THE AUTHORING RULE. Read this before touching a vertex.
-------------------------------------------------------------------------------
Every holder is modelled inside the UNIT CUBE, because the builder scales it
non-uniformly straight onto the item's measured box, one axis at a time:

    +X   the stretch axis — the item's LENGTH.  -0.5 .. +0.5
    +Z   across            — the item's WIDTH.  -0.5 .. +0.5
    +Y   up out of the surface — the item's HEIGHT.  0.0 .. 1.0

so the origin is the gripping centre lying ON the surface plane: y = 0 is the
mat, y = 1 is the top of the item's bounding box. All three axes are normalised,
not just two — "two shock-cord rings at 25% and 75% height" only means anything
if height is 1.00 m here, or a ring authored at y = 0.25 lands a quarter of the
way up whatever it grips rather than at the quarter mark of the item.

Every RIGID part — buckles, hooks, tensioners, snap gates, eyelets — hangs off an
empty whose name starts `HARD_`, which the builder counter-scales. Rigid parts
are therefore authored at their TRUE METRE SIZE (a buckle is ~0.05 m, not ~0.05
of a unit cube), and they come out that size on a 0.26 m Leash and on a 1.35 m
LaserStaff alike. Without the counter-scale a strap spanning the staff arrives
with buckles the size of dinner plates, and it fails looking like a modelling
mistake rather than a code one.

Two consequences that are easy to trip over:

  * **`HARD_` empties are never rotated relative to the holder root.** The
    counter-scale is a componentwise reciprocal, which only inverts a
    non-uniform scale when the child's axes line up with the parent's; under a
    rotation the two do not commute and the part comes out sheared rather than
    restored. Where a buckle has to sit at an angle, the rotation is baked into
    the MESH inside the empty and the empty itself stays at identity.
  * **Soft parts do stretch, and that is intended.** Webbing spanning a 1.35 m
    staff really is 1.35 m of webbing. Its width and thickness stretch too,
    which is the honest cost of the scheme; only hardware is protected.

No colliders and no components: holders are stripped at runtime, and a holder
the cursor could hit would shadow the item sitting in it.

The five, and what picks them (spec 3.5)
----------------------------------------
  Holder_Cord     tall & round      two shock-cord rings at 25% / 75% height
  Holder_Webbing  long & thin       two tape straps at 25% / 75% of length
  Holder_Bungee   irregular         four-point elastic X with a centre hook
  Holder_Sleeve   tool profile      open-topped scabbard, handle proud of it
  Holder_Clip     small             snap hook on a tape tail; the item hangs

One collection each, so `_preview.py --spread` lays all five out in a row.

    blender --background --python pack_holders.py -- --out pack_holders.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, LIB)
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Fabric_Canvas_Faded",    # 0  scabbard body, clip tail
    "Mat_Fabric_Wing_Ochre",      # 1  webbing tape — a shade off the rig's panels
    "Mat_Metal_Steel_Worn",       # 2  hooks, snap gates
    "Mat_Metal_Brass_Tarnished",  # 3  buckles, tensioners, eyelets
    "Mat_Plastic_Rubber_Black",   # 4  shock cord, bungee
]
CANVAS, OCHRE, STEEL, BRASS, RUBBER = range(5)

# True metre sizes for the counter-scaled hardware. These are the numbers the
# whole HARD_ mechanism exists to protect, so they live together.
SZ_BUCKLE = 0.052
SZ_TENSIONER = 0.046
SZ_HOOK = 0.070
SZ_SNAP = 0.092
SZ_EYELET = 0.030


# ---------------------------------------------------------------------------
# Helpers — copies, as both pack scripts carry their own
# ---------------------------------------------------------------------------

def bent_tube(p, pts, r, mat, seg=6, collar=False):
    """A tube following a polyline, with an optional weld collar at each kink."""
    pts = [Vector(q) for q in pts]
    for a, b in zip(pts, pts[1:]):
        d = b - a
        if d.length < 1e-6:
            continue
        rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        p.cyl((a + b) / 2.0, r, d.length, seg=seg, mat=mat, rot=rot)
    if collar:
        for i in range(1, len(pts) - 1):
            d = pts[i + 1] - pts[i - 1]
            rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
            p.cyl(pts[i], r * 1.22, r * 1.4, seg=seg, mat=mat, rot=rot)


def ribbon(p, pts, width, thick, mat, flat='X'):
    """Webbing along a polyline — `width` lies along `flat`, `thick` across it."""
    for a, b in zip(pts, pts[1:]):
        p.seam(a, b, width=thick, depth=width, axis=flat, mat=mat)


def loop_buckle(p, c, w, h, t, mat, rot=None, bar=True):
    """A rectangular hardware loop with a centre bar — strap threads through it.

    `w` across, `h` along the strap, `t` the section. `rot` is baked into the
    geometry rather than applied to the parent empty, because a rotated `HARD_`
    empty shears under the builder's componentwise counter-scale.
    """
    c = Vector(c)
    m = (rot or Matrix.Identity(4)).to_3x3()
    for sy in (-1, 1):
        p.box(c + m @ Vector((0.0, sy * (h / 2 - t / 2), 0.0)),
              (w, t, t), mat, rot=rot)
    for sx in (-1, 1):
        p.box(c + m @ Vector((sx * (w / 2 - t / 2), 0.0, 0.0)),
              (t, h, t), mat, rot=rot)
    if bar:
        p.box(c, (w - 2 * t, t * 0.7, t * 0.7), mat, rot=rot)


def stitches(p, a, b, count, mat, size):
    """A dashed thread run. Cheapest thing that reads as sewn rather than moulded."""
    a, b = Vector(a), Vector(b)
    d = b - a
    for i in range(count):
        p.box(a + d * ((i + 0.5) / count), size, mat)


def strap_frame(d, n):
    """A rotation whose local X is across the strap, Y along it, Z its normal."""
    d = Vector(d).normalized()
    n = Vector(n).normalized()
    s = d.cross(n).normalized()
    n = s.cross(d).normalized()
    return Matrix((s, d, n)).transposed().to_4x4()


def xform(rot, c, pts):
    m = (rot or Matrix.Identity(4)).to_3x3()
    return [tuple(Vector(c) + m @ Vector(q)) for q in pts]


def circle(r, n, plane='YZ', cy=0.0, cz=0.0, a0=0.0, a1=360.0):
    """A polyline arc in a local plane — the raw material of every hook here."""
    out = []
    for i in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        u, v = cy + r * math.cos(a), cz + r * math.sin(a)
        out.append((0.0, u, v) if plane == 'YZ' else (u, 0.0, v))
    return out


def snap_hook(p, c, rot, L, body=STEEL, gate=BRASS):
    """A steel snap hook: 300 degrees of bar, a spring gate across the gap, an eye.

    Authored at a true length of `L` metres, which is the point — this sits
    under a `HARD_` empty and comes out `L` long on every item.
    """
    r = L * 0.26
    cy = L * 0.24
    arc = circle(r, 11, cy=cy, a0=-52.0, a1=248.0)
    bent_tube(p, xform(rot, c, arc), L * 0.052, body, seg=6)
    bent_tube(p, xform(rot, c, [arc[0], arc[-1]]), L * 0.036, gate, seg=6)
    bent_tube(p, xform(rot, c, [(0.0, cy - r - L * 0.01, 0.0),
                                (0.0, -L * 0.30, 0.0)]), L * 0.050, body, seg=6)
    bent_tube(p, xform(rot, c, circle(L * 0.085, 7, cy=-L * 0.30)),
              L * 0.026, body, seg=5)


def side_release(p, c, rot, L):
    """A side-release buckle: brass shell, two sprung prongs, a ladder-lock tail.

    The offset-to-one-side buckle the spec asks for. Reads at a glance because
    the prong slots break its outline; a plain rectangle at this size reads as a
    washer.
    """
    m = (rot or Matrix.Identity(4)).to_3x3()
    c = Vector(c)
    p.box(c, (L * 1.15, L * 0.72, L * 0.30), BRASS, rot=rot)
    for sx in (-1, 1):
        p.box(c + m @ Vector((sx * L * 0.44, L * 0.54, 0.0)),
              (L * 0.20, L * 0.46, L * 0.22), RUBBER, rot=rot)
    p.box(c + m @ Vector((0.0, L * 0.62, 0.0)),
          (L * 0.34, L * 0.40, L * 0.20), RUBBER, rot=rot)
    p.box(c + m @ Vector((0.0, -L * 0.52, 0.0)),
          (L * 1.05, L * 0.34, L * 0.22), BRASS, rot=rot)


def eyelet(p, c, L, mat=BRASS):
    """A punched eyelet with its washer — how soft webbing meets the mat.

    Always horizontal: every one of these sits on the surface plane, and the
    hole has to face up out of it.
    """
    c = Vector(c)
    p.tube(c, L * 0.42, L * 0.14, L * 0.55, axis='Y', seg=8, mat=mat)
    p.cyl(c - Vector((0.0, L * 0.24, 0.0)), L * 0.62, L * 0.14, axis='Y',
          seg=8, mat=mat)


def empty(name, loc, coll, size=0.06, display='ARROWS'):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = display
    obj.empty_display_size = size
    obj.location = Vector(loc)
    coll.objects.link(obj)
    return obj


def attach(child, parent, parent_world):
    """Parent with a clean local transform.

    Blender's parent-inverse hides the offset in a matrix the FBX flattens away.
    Only valid because no parent here is rotated — which for `HARD_` empties is
    a hard requirement, not a convenience.
    """
    child.parent = parent
    child.matrix_parent_inverse = Matrix.Identity(4)
    child.location = Vector(child.location) - Vector(parent_world)
    return child


# ---------------------------------------------------------------------------
# Registry
# ---------------------------------------------------------------------------
#
# A part declares where its origin is and, if it is rigid, the `HARD_` empty it
# belongs under. Soft parts hang straight off the holder root and stretch with
# the item.

HOLDERS = []


def holder(name, note):
    def wrap(fn):
        HOLDERS.append((name, note, fn))
        return fn
    return wrap


def emit(coll, root, mats, parts):
    """Build a holder's part list, creating `HARD_` empties as they are named."""
    hards = {}
    for pname, fn, origin, hard, bevel in parts:
        p = Part(mats)
        fn(p)
        if bevel:
            p.bevel(width=bevel, segments=1)
        obj = p.finish(pname, coll, origin=origin)

        if hard is None:
            attach(obj, root, (0.0, 0.0, 0.0))
            continue

        hname, hloc = hard
        if hname not in hards:
            e = empty(hname, hloc, coll, size=0.05, display='PLAIN_AXES')
            attach(e, root, (0.0, 0.0, 0.0))
            hards[hname] = (e, hloc)
        host, hostloc = hards[hname]
        attach(obj, host, hostloc)
    return hards


# ---------------------------------------------------------------------------
# Holder_Cord — tall & round
# ---------------------------------------------------------------------------

CORD_Y = (0.25, 0.75)


@holder("Holder_Cord", "tall & round: height > 1.5x width, roughly circular")
def _cord(mats):
    """Two shock-cord rings cinched round the item, a tensioner barrel on each.

    The rings are true circles of the unit footprint, so on an item that is not
    quite round they arrive as the ellipse that item actually is — which is the
    scheme working, not a defect.
    """
    parts = []

    for tag, gy in zip(("Lo", "Hi"), CORD_Y):
        def ring(p, gy=gy):
            p.torus((0.0, gy, 0.0), 0.500, 0.020, axis='Y', maj_seg=20,
                    min_seg=6, mat=RUBBER)
        parts.append(("Mesh_Cord_Ring_" + tag, ring, (0.0, gy, 0.0), None, 0.0))

        tc = (0.0, gy, -0.520)

        def tens(p, tc=tc):
            # Barrel along the cord — which at the ring's front runs in X —
            # with a rubber thumb tab standing out of the surface where the
            # focus camera can see it.
            c = Vector(tc)
            L = SZ_TENSIONER
            p.cyl(c, L * 0.32, L, axis='X', seg=10, mat=BRASS)
            for sx in (-1, 1):
                p.cyl(c + Vector((sx * L * 0.54, 0.0, 0.0)), L * 0.40,
                      L * 0.16, axis='X', seg=10, mat=BRASS)
            p.box(c + Vector((0.0, L * 0.38, 0.0)),
                  (L * 0.70, L * 0.36, L * 0.30), RUBBER)
        parts.append(("Mesh_Cord_Tensioner_" + tag, tens, tc,
                      ("HARD_Cord_Tensioner_" + tag, tc), 0.004))

    def tails(p):
        for sx in (-1, 1):
            bent_tube(p, [(sx * 0.492, CORD_Y[0], 0.0),
                          (sx * 0.500, CORD_Y[1], 0.0)], 0.016, RUBBER)
            bent_tube(p, [(sx * 0.492, CORD_Y[0], 0.0),
                          (sx * 0.470, 0.130, 0.0),
                          (sx * 0.440, 0.030, 0.0)], 0.016, RUBBER)
    parts.append(("Mesh_Cord_Tails", tails, (0.0, 0.25, 0.0), None, 0.0))

    for sx, side in ((-1, "L"), (1, "R")):
        ac = (sx * 0.440, 0.014, 0.0)

        def anchor(p, ac=ac):
            eyelet(p, ac, SZ_EYELET)
        parts.append(("Mesh_Cord_Anchor_" + side, anchor, ac,
                      ("HARD_Cord_Anchor_" + side, ac), 0.003))

    return parts


# ---------------------------------------------------------------------------
# Holder_Webbing — long & thin
# ---------------------------------------------------------------------------

WEB_X = (-0.250, 0.250)

# The strap path hugs a box rather than arcing over it: a smooth arc across a
# rectangular item leaves daylight at the shoulders and reads as a hoop.
WEB_PATH = ((0.0, 0.020, -0.540), (0.0, 0.540, -0.545), (0.0, 0.960, -0.430),
            (0.0, 1.045, -0.150), (0.0, 1.045, 0.150), (0.0, 0.960, 0.430),
            (0.0, 0.540, 0.545), (0.0, 0.020, 0.540))


@holder("Holder_Webbing", "long & thin: length > 3x the other two axes")
def _webbing(mats):
    """Two tape straps at 25% / 75% of length, buckles offset to one side.

    Both buckles on -Z on purpose: one hand reaches one side and both come
    undone. Alternating them would look better and work worse.
    """
    parts = []

    for tag, gx in zip(("A", "B"), WEB_X):
        def strap(p, gx=gx):
            ribbon(p, [(gx, q[1], q[2]) for q in WEB_PATH], 0.110, 0.024,
                   OCHRE, flat='X')
        parts.append(("Mesh_Web_Strap_" + tag, strap, (gx, 0.5, 0.0), None, 0.004))

        bc = (gx, 0.740, -0.556)

        def buckle(p, bc=bc):
            side_release(p, bc, strap_frame((0.0, 0.62, 0.22),
                                            (0.0, 0.22, -0.62)), SZ_BUCKLE)
        parts.append(("Mesh_Web_Buckle_" + tag, buckle, bc,
                      ("HARD_Web_Buckle_" + tag, bc), 0.003))

        for sz, side in ((-1, "N"), (1, "P")):
            ec = (gx, 0.012, sz * 0.540)

            def anchor(p, ec=ec):
                eyelet(p, ec, SZ_EYELET)
            parts.append(("Mesh_Web_Anchor_%s%s" % (tag, side), anchor, ec,
                          ("HARD_Web_Anchor_%s%s" % (tag, side), ec), 0.003))

    return parts


# ---------------------------------------------------------------------------
# Holder_Bungee — irregular
# ---------------------------------------------------------------------------

BUNGEE_CORNERS = ((-0.470, -0.470), (0.470, -0.470), (0.470, 0.470),
                  (-0.470, 0.470))


@holder("Holder_Bungee", "irregular: no dominant axis")
def _bungee(mats):
    """A four-point elastic X pulled down onto whatever shape is under it.

    Each leg dips INSIDE the unit box on its way to the hook, so the cord reads
    as stretched over the item and biting into it rather than tented above it.
    That indentation is most of what sells this holder.
    """
    parts = []

    def cords(p):
        # TWO cords crossing, not four meeting at a point. Four legs converging
        # on one vertex reads as a pinch or a tent pole; a crossed pair with a
        # hook clamping the crossing reads as one elastic doubled over the item,
        # which is what it is.
        for k, (dy, run) in enumerate((
                (0.000, ((-0.470, -0.470), (0.470, 0.470))),
                (-0.036, ((0.470, -0.470), (-0.470, 0.470))))):
            (ax, az), (bx, bz) = run
            bent_tube(p, [
                (ax, 0.030, az),
                (ax * 0.78, 0.410, az * 0.78),
                (ax * 0.40, 0.760 + dy, az * 0.40),
                (ax * 0.13, 0.870 + dy, az * 0.13),
                (bx * 0.13, 0.870 + dy, bz * 0.13),
                (bx * 0.40, 0.760 + dy, bz * 0.40),
                (bx * 0.78, 0.410, bz * 0.78),
                (bx, 0.030, bz)], 0.019, RUBBER, seg=6)
    parts.append(("Mesh_Bungee_Cords", cords, (0.0, 0.5, 0.0), None, 0.0))

    hc = (0.0, 0.898, 0.0)

    def hook(p):
        rot = strap_frame((0.0, 1.0, 0.0), (0.0, 0.0, -1.0))
        m = rot.to_3x3()
        p.cyl(Vector(hc), SZ_HOOK * 0.22, SZ_HOOK * 0.26, axis='Z', seg=10,
              mat=STEEL, rot=rot)
        arc = circle(SZ_HOOK * 0.34, 9, cy=SZ_HOOK * 0.46, a0=-70.0, a1=250.0)
        bent_tube(p, xform(rot, hc, arc), SZ_HOOK * 0.075, STEEL, seg=6)
        p.box(Vector(hc) + m @ Vector((0.0, SZ_HOOK * 0.16, 0.0)),
              (SZ_HOOK * 0.62, SZ_HOOK * 0.16, SZ_HOOK * 0.16), BRASS, rot=rot)
    parts.append(("Mesh_Bungee_Hook", hook, hc, ("HARD_Bungee_Hook", hc), 0.003))

    for i, (cx, cz) in enumerate(BUNGEE_CORNERS):
        ac = (cx, 0.014, cz)

        def anchor(p, ac=ac):
            eyelet(p, ac, SZ_EYELET)
        parts.append(("Mesh_Bungee_Anchor_%d" % i, anchor, ac,
                      ("HARD_Bungee_Anchor_%d" % i, ac), 0.003))

    return parts


# ---------------------------------------------------------------------------
# Holder_Sleeve — tool profile
# ---------------------------------------------------------------------------

SLV_X0, SLV_X1 = -0.560, 0.060     # the scabbard covers the head end
SLV_H = 0.680
SLV_STATIONS = (SLV_X0, SLV_X0 + 0.080, -0.320, -0.130, SLV_X1)


def slv_h(x):
    """Wall height along the scabbard: deep at the toe, cut away at the mouth.

    A constant-height channel reads as a box with a slot in it. The taper is
    what makes it a scabbard, and it is also what lets the item's handle stand
    proud where the eye expects a handle to be.
    """
    t = (x - SLV_X0) / (SLV_X1 - SLV_X0)
    return SLV_H * (1.0 - 0.44 * t * t)


@holder("Holder_Sleeve", "tool profile: long, with its mass at one end")
def _sleeve(mats):
    """An open-topped canvas scabbard with the handle standing proud of its mouth.

    Built as a floor, two walls and an end cap rather than as one lofted trough:
    a trough's cross-section is a C, and a C caps into a concave n-gon that
    triangulates into overlapping faces on FBX export.

    The scabbard is at -X because the shape test that picks this holder is "mass
    at one end", and the heavy end is the one worth sheathing.
    """
    parts = []

    def body(p):
        p.slab((SLV_X0, 0.0, -0.450), (SLV_X1, 0.080, 0.450), CANVAS)
        for sz in (-1, 1):
            z0, z1 = sorted((sz * 0.450, sz * 0.514))
            p.loft([(x, [(0.0, z0), (slv_h(x), z0), (slv_h(x), z1), (0.0, z1)])
                    for x in SLV_STATIONS], axis='X', mat=CANVAS, cap=True)
        p.slab((SLV_X0, 0.0, -0.514), (SLV_X0 + 0.066, slv_h(SLV_X0), 0.514),
               CANVAS)
    parts.append(("Mesh_Sleeve_Body", body, (0.0, 0.0, 0.0), None, 0.006))

    def mouth(p):
        # Rolled binding down the tapered top edge and round the mouth, so a cut
        # canvas edge is never what the player is looking at.
        for sz in (-1, 1):
            bent_tube(p, [(x, slv_h(x) - 0.014, sz * 0.482)
                          for x in SLV_STATIONS], 0.030, OCHRE, seg=6)
            bent_tube(p, [(SLV_X1 - 0.014, slv_h(SLV_X1) - 0.020, sz * 0.486),
                          (SLV_X1 - 0.014, 0.074, sz * 0.486)], 0.028, OCHRE,
                      seg=6)
        bent_tube(p, [(SLV_X0 + 0.024, slv_h(SLV_X0) - 0.014, -0.482),
                      (SLV_X0 + 0.024, slv_h(SLV_X0) - 0.014, 0.482)],
                  0.030, OCHRE, seg=6)
        for sz in (-1, 1):
            for gx in (-0.410, -0.170):
                ribbon(p, [(gx, 0.050, sz * 0.508),
                           (gx, slv_h(gx) * 0.62, sz * 0.520),
                           (gx, slv_h(gx) - 0.030, sz * 0.492)],
                       0.086, 0.020, OCHRE, flat='X')
            stitches(p, (SLV_X0 + 0.070, 0.106, sz * 0.500),
                     (SLV_X1 - 0.040, 0.106, sz * 0.500), 9, OCHRE,
                     (0.030, 0.012, 0.012))
    parts.append(("Mesh_Sleeve_Mouth", mouth, (0.0, 0.0, 0.0), None, 0.005))

    def strap(p):
        ribbon(p, [(0.340, q[1], q[2]) for q in WEB_PATH], 0.100, 0.022, OCHRE,
               flat='X')
    parts.append(("Mesh_Sleeve_Strap", strap, (0.340, 0.5, 0.0), None, 0.004))

    bc = (0.340, 0.740, -0.556)

    def buckle(p):
        side_release(p, bc, strap_frame((0.0, 0.62, 0.22), (0.0, 0.22, -0.62)),
                     SZ_BUCKLE)
    parts.append(("Mesh_Sleeve_Buckle", buckle, bc,
                  ("HARD_Sleeve_Buckle", bc), 0.003))

    for sz, side in ((-1, "N"), (1, "P")):
        ec = (0.340, 0.012, sz * 0.540)

        def anchor(p, ec=ec):
            eyelet(p, ec, SZ_EYELET)
        parts.append(("Mesh_Sleeve_Anchor_" + side, anchor, ec,
                      ("HARD_Sleeve_Anchor_" + side, ec), 0.003))

    return parts


# ---------------------------------------------------------------------------
# Holder_Clip — small
# ---------------------------------------------------------------------------

CLIP_TAIL = ((0.0, 0.030, 0.470), (0.0, 0.400, 0.500), (0.0, 0.760, 0.430),
             (0.0, 0.985, 0.250))


@holder("Holder_Clip", "small: longest axis under 0.12 m")
def _clip(mats):
    """A snap hook on a short tape tail. The item hangs off it rather than lying.

    The tail runs up BEHIND the item's box, so the hook comes over the top and
    nothing crosses the face the player is looking at. Small items are the ones
    hardest to pick out of a laid-out kit; a clip that hides them under its own
    hardware defeats the point.
    """
    parts = []

    def tail(p):
        ribbon(p, CLIP_TAIL, 0.190, 0.038, CANVAS, flat='X')
    parts.append(("Mesh_Clip_Tail", tail, (0.0, 0.5, 0.45), None, 0.005))

    hc = (0.0, 1.010, 0.170)

    def hook(p):
        snap_hook(p, hc, strap_frame((0.0, -0.55, -0.84), (0.0, -0.84, 0.55)),
                  SZ_SNAP)
    parts.append(("Mesh_Clip_Hook", hook, hc, ("HARD_Clip_Hook", hc), 0.003))

    ac = (0.0, 0.014, 0.470)

    def anchor(p):
        eyelet(p, ac, SZ_EYELET * 1.2)
    parts.append(("Mesh_Clip_Anchor", anchor, ac, ("HARD_Clip_Anchor", ac), 0.003))

    return parts


# ---------------------------------------------------------------------------
# Verification
# ---------------------------------------------------------------------------

def dump_holders():
    """Print each holder's envelope and ASSERT the authoring rule.

    Four things go wrong here in ways nothing in Blender shows: a holder that
    overruns the unit cube, a rigid part that is not under a `HARD_` empty, a
    `HARD_` empty that is rotated, and hardware authored at unit-cube scale
    instead of at true metres. The first three are checked; the fourth is why
    the true sizes are printed.
    """
    bpy.context.view_layer.update()
    bad = []
    print("  --- holders: unit-cube envelope, x +-0.5, z +-0.5, y 0..1 ---")

    for name, note, _ in HOLDERS:
        root = bpy.data.objects[name]
        kids = [o for o in bpy.data.objects if o.type == 'MESH'
                and _under(o, root)]
        pts = [o.matrix_world @ Vector(c) for o in kids for c in o.bound_box]
        lo = Vector((min(q.x for q in pts), min(q.y for q in pts),
                     min(q.z for q in pts)))
        hi = Vector((max(q.x for q in pts), max(q.y for q in pts),
                     max(q.z for q in pts)))
        hard = [o for o in bpy.data.objects
                if o.name.startswith("HARD_") and _under(o, root)]
        print("    %-16s x %+0.3f..%+0.3f  y %+0.3f..%+0.3f  z %+0.3f..%+0.3f"
              "   %d meshes, %d HARD  (%s)"
              % (name, lo.x, hi.x, lo.y, hi.y, lo.z, hi.z, len(kids),
                 len(hard), note))

        if lo.y < -0.04:
            bad.append("%s dips below the surface plane (y=%.3f)" % (name, lo.y))
        if hi.x > 0.70 or lo.x < -0.70 or hi.z > 0.70 or lo.z < -0.70:
            bad.append("%s overruns the unit footprint" % name)
        if hi.y > 1.30:
            bad.append("%s overruns the unit height (y=%.3f)" % (name, hi.y))
        if not hard:
            bad.append("%s has no HARD_ empty — nothing would be counter-scaled"
                       % name)

    for o in bpy.data.objects:
        if not o.name.startswith("HARD_"):
            continue
        r = o.rotation_euler
        if abs(r.x) + abs(r.y) + abs(r.z) > 1e-6:
            bad.append("%s is ROTATED — the counter-scale would shear it" % o.name)
        s = o.scale
        if abs(s.x - 1) + abs(s.y - 1) + abs(s.z - 1) > 1e-6:
            bad.append("%s is not identity-scaled" % o.name)
        if not [c for c in o.children if c.type == 'MESH']:
            bad.append("%s has no mesh under it" % o.name)

    for o in bpy.data.objects:
        if o.type != 'MESH':
            continue
        if o.parent is not None and o.parent.name.startswith("HARD_"):
            d = o.dimensions
            print("    HARD  %-30s true size (%.3f, %.3f, %.3f) m"
                  % (o.name, d.x, d.y, d.z))
            if max(d) > 0.18:
                bad.append("%s is %.3f m — hardware should be centimetres, and "
                           "a unit-cube-scaled part here is exactly the "
                           "dinner-plate bug" % (o.name, max(d)))

    print("  hardware true sizes: buckle %.3f  tensioner %.3f  hook %.3f  "
          "snap %.3f  eyelet %.3f m"
          % (SZ_BUCKLE, SZ_TENSIONER, SZ_HOOK, SZ_SNAP, SZ_EYELET))

    if bad:
        raise SystemExit("Holder authoring rule violated:\n  "
                         + "\n  ".join(bad))


def _under(obj, root):
    o = obj
    while o is not None:
        if o is root:
            return True
        o = o.parent
    return False


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, _note, fn in HOLDERS:
        coll = collection("Coll_" + name)
        root = empty(name, (0.0, 0.0, 0.0), coll, size=0.30)
        emit(coll, root, mats, fn(mats))

    report()
    dump_holders()
    save(out)


main()
