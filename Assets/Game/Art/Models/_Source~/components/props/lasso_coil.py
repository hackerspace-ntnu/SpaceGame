"""Lariat coils — the carried rope behind the Lasso artifact.

The Lasso had no held model at all. Its prefab pointed at the thrown rope: a
4.4 m Bezier curve authored at flight scale, which is the right geometry for a
rope in the air and hopeless as a thing in a fist. Sized down to the hand it
became a hair; left alone it dragged four metres of mesh through the world.
This file is the missing object — the rope as it looks *before* it is thrown.

Deliberately fibre rather than tech. `leash_device.blend` already owns the
engineered answer to catching something (a spool, a fairlead, a snap hook), and
two hand-sized coils of cable would be indistinguishable at icon size. A laid
rope with a whipped end reads as the improvised counterpart to it.

Built by sweeping one continuous path per coil rather than stacking tori. A coil
is a helix that closes back on itself, and drawing it as separate rings leaves
visible gaps end-on where the turns should run into each other. The sweep helper
is lifted from `portal_gun.py` — `_buildlib` has none of its own, and the two
copies now in the library are a sign it belongs there.

Sized as carried equipment, 0.22-0.26 m across, matching the rest of the artifact
device family. Origin sits where the hand closes on the rope, so the Unity
ItemGrip marker is the object's own origin.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

# Index 0 first: `bmesh.ops.bevel` stamps new faces with material index 0, so
# whatever sits here colours every chamfered edge in the file. Rope is the
# dominant surface on all three variations, so it takes the slot.
HEMP, CORD, LEATHER, STEEL, BRASS = range(5)
MATS = ["Mat_Fabric_Rope_Hemp", "Mat_Fabric_Canvas_Faded",
        "Mat_Fabric_Seat_Ochre", "Mat_Metal_Steel_Worn",
        "Mat_Metal_Brass_Tarnished"]

ROPE_R = 0.0085          # 17 mm laid rope — a believable lariat stock
ROPE_SEG = 7             # segments around the rope's own cross-section


def sweep(p, pts, radius, mat, seg=ROPE_SEG, closed=False):
    """A swept tube through a polyline, using parallel-transport frames.

    Copied from `portal_gun.py`, with a `closed` option added so a coil can run
    continuously into itself. Transporting the normal from ring to ring rather
    than rebuilding it per segment is what stops the tube twisting where the
    path leaves a plane — which a helix does everywhere.
    """
    pts = [Vector(q) for q in pts]
    n_pts = len(pts)

    tangents = []
    for i in range(n_pts):
        if closed:
            t = pts[(i + 1) % n_pts] - pts[(i - 1) % n_pts]
        elif i == 0:
            t = pts[1] - pts[0]
        elif i == n_pts - 1:
            t = pts[-1] - pts[-2]
        else:
            t = pts[i + 1] - pts[i - 1]
        tangents.append(t.normalized())

    seed = Vector((0.0, 0.0, 1.0))
    if abs(tangents[0].dot(seed)) > 0.95:
        seed = Vector((0.0, 1.0, 0.0))
    normal = (seed - tangents[0] * seed.dot(tangents[0])).normalized()

    rings = []
    for i, (q, t) in enumerate(zip(pts, tangents)):
        if i > 0:
            prev = tangents[i - 1]
            axis = prev.cross(t)
            if axis.length > 1e-6:
                normal = (Matrix.Rotation(prev.angle(t), 4, axis.normalized())
                          @ normal)
            normal = (normal - t * normal.dot(t)).normalized()
        binormal = t.cross(normal).normalized()
        rings.append([
            q + normal * (math.cos(2 * math.pi * k / seg) * radius)
              + binormal * (math.sin(2 * math.pi * k / seg) * radius)
            for k in range(seg)])

    bm2 = bmesh.new()
    vrings = [[bm2.verts.new(tuple(c)) for c in ring] for ring in rings]
    pairs = list(zip(vrings, vrings[1:]))
    if closed:
        pairs.append((vrings[-1], vrings[0]))
    for a, b in pairs:
        for i in range(seg):
            j = (i + 1) % seg
            bm2.faces.new((a[i], a[j], b[j], b[i]))
    if not closed:
        bm2.faces.new(vrings[0])
        bm2.faces.new(list(reversed(vrings[-1])))

    faces = p._absorb(bm2, mat)
    for f in faces:
        f.smooth = True
    return faces


def _coil_path(centre, major, turns, spread, rng, taper=0.0, wobble=0.006,
               steps=30):
    """A helix that lies down as a coil: `turns` wraps stacked along Z.

    The wobble is what stops it reading as a machined spring. Real rope laid in
    a hank never repeats a turn exactly, and at this scale a couple of
    millimetres of drift per turn is the whole difference.
    """
    cx, cy, cz = centre
    pts = []
    total = turns * steps
    # Per-turn drift, interpolated so the path stays smooth rather than
    # stepping between turns.
    drift = [(rng.uniform(-wobble, wobble), rng.uniform(-wobble, wobble),
              rng.uniform(-0.0025, 0.0025)) for _ in range(turns + 2)]
    for i in range(total):
        u = i / total                       # 0..1 along the whole coil
        a = 2 * math.pi * turns * u
        k = u * turns
        i0 = int(k)
        f = k - i0
        dx = drift[i0][0] * (1 - f) + drift[i0 + 1][0] * f
        dy = drift[i0][1] * (1 - f) + drift[i0 + 1][1] * f
        dr = drift[i0][2] * (1 - f) + drift[i0 + 1][2] * f
        # Ends pull in, middle sits proud — a hank hangs fatter in the middle.
        shrink = taper * abs(u - 0.5) * 2.0
        r = major * (1.0 - shrink) + dr
        pts.append((cx + math.cos(a) * r + dx,
                    cy + math.sin(a) * r + dy,
                    cz - spread / 2 + spread * u))
    return pts


def _whipping(p, centre, major, width, mat=CORD, minor=None, n=5, axis='Y'):
    """The binding that holds a coil together — cord wrapped round the bundle.

    The wrap has to encircle the rope, which means its axis runs *along* the
    bundle at the point it is tied — tangential to the coil, not parallel to the
    coil's own axis. Ringing it the other way produces two discs either side of
    the turns, and the thing stops being a bound coil and becomes a cable spool
    with flanges, which is exactly the `leash_device` silhouette this model is
    supposed to be distinguishable from.

    Without a binding of some kind the turns read as a loose pile, and it is the
    only hard edge on an otherwise entirely round object — the eye needs one to
    fix the scale by.
    """
    minor = ROPE_R * 0.42 if minor is None else minor
    off = {'X': 0, 'Y': 1, 'Z': 2}[axis]
    faces = []
    for i in range(n):
        c = list(centre)
        c[off] += -width / 2 + width * (i + 0.5) / n
        faces += p.torus(tuple(c), major, minor, axis, 16, 6, mat)
    return faces


def _honda(p, centre, radius, tilt, ring=True):
    """The throwing loop, kept small and tucked against the coil.

    A real honda runs metres across. That cannot be on a held model — anything
    hanging clips the leg and the ground while walking, which is the whole
    reason this file exists. So it is drawn as the *knot* end: a hand-sized eye
    with its hardware, which reads as a lasso without extending the silhouette.
    """
    cx, cy, cz = centre
    pts = []
    steps = 22
    for i in range(steps):
        a = 2 * math.pi * i / steps
        u, v = math.cos(a) * radius, math.sin(a) * radius
        # Tip the eye out of plane so it sits against the coil rather than
        # standing off it like a hoop.
        pts.append((cx + u, cy + v * math.cos(tilt), cz + v * math.sin(tilt)))
    faces = sweep(p, pts, ROPE_R * 0.92, HEMP, closed=True)
    if ring:
        # The brass eyelet the rope runs through. Small, but it is the detail
        # that says "lasso" rather than "bundle of rope".
        faces += p.torus((cx, cy - radius * math.cos(tilt),
                          cz - radius * math.sin(tilt)),
                         ROPE_R * 1.5, ROPE_R * 0.45, 'X', 12, 6, BRASS)
    return faces


def _tail(p, start, drop, mat=HEMP):
    """A short run of rope leaving the bundle, ending in a whipped tip."""
    sx, sy, sz = start
    pts = [(sx, sy, sz),
           (sx + 0.006, sy - 0.004, sz - drop * 0.45),
           (sx + 0.004, sy - 0.010, sz - drop * 0.80),
           (sx - 0.002, sy - 0.012, sz - drop)]
    faces = sweep(p, pts, ROPE_R, mat)
    faces += p.torus((sx - 0.002, sy - 0.012, sz - drop + 0.006),
                     ROPE_R * 0.7, ROPE_R * 0.5, 'Z', 10, 6, CORD)
    return faces


# ---------------------------------------------------------------------------
# Variations. Each differs in silhouette first, per the library's variation
# rule — a flat hank, a doubled bundle and a slung coil, not one coil in three
# tints.
# ---------------------------------------------------------------------------

def build_coil(p, rng):
    """Coil — the working lariat, gathered into a flat hank.

    The one wired to the artifact. Widest and shallowest of the three, so it
    presents its full circle to camera when held.
    """
    sweep(p, _coil_path((0, 0, 0), 0.108, 7, 0.050, rng, taper=0.14),
          ROPE_R, HEMP)
    # Tied at the +X side, wrapping the full depth of the turn stack.
    _whipping(p, (0.108, 0, 0), 0.032, 0.024, n=4)
    _honda(p, (-0.082, 0.012, 0.028), 0.034, tilt=0.55)
    _tail(p, (-0.098, -0.018, -0.016), 0.044)


def build_hank(p, rng):
    """Hank — the rope doubled and bound at the waist.

    Taller, narrower, figure-of-eight in profile: two lobes pinched by one
    binding. Reads as stowed rope rather than rope ready to throw.
    """
    for sign in (-1, 1):
        sweep(p, _coil_path((0, 0, sign * 0.046), 0.068, 5, 0.038, rng,
                            taper=0.20), ROPE_R, HEMP)
    # One tie gathering both lobes at the waist, wrapping round the outside of
    # the doubled bundle rather than sitting between the lobes like a spool core.
    _whipping(p, (0.068, 0, 0), 0.076, 0.022, n=4)
    _honda(p, (0.054, 0.010, 0.082), 0.028, tilt=1.05)
    _tail(p, (-0.052, 0.012, -0.066), 0.036)


def build_saddle(p, rng):
    """Saddle — a loose coil hung on a leather keeper.

    Slung gear rather than held gear: the rope sits in a strap with a buckle,
    the way a lariat rides on a saddle horn. Built ahead, not wired to anything.
    """
    sweep(p, _coil_path((0, 0, -0.008), 0.092, 6, 0.058, rng, taper=0.08,
                        wobble=0.009), ROPE_R, HEMP)

    # A leather keeper closed round the bundle, with a buckle sitting proud on
    # the outside. Wide and flat where the Coil's tie is thin cord, so the two
    # read as different fastenings and not as the same band in another colour.
    _whipping(p, (0.092, 0, 0), 0.036, 0.030, mat=LEATHER, minor=0.0055, n=3)
    p.box((0.126, 0, 0), (0.012, 0.026, 0.018), STEEL)
    p.torus((0.132, 0, 0), 0.010, 0.0032, 'X', 12, 6, STEEL)

    _honda(p, (-0.070, 0.012, -0.038), 0.030, tilt=0.35)


VARIATIONS = [
    ("Coll_Lasso_Coil", build_coil, 4041),
    ("Coll_Lasso_Hank", build_hank, 4042),
    ("Coll_Lasso_Saddle", build_saddle, 4043),
]


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for coll_name, builder, seed in VARIATIONS:
        coll = collection(coll_name)
        p = Part(mats)
        builder(p, random.Random(seed))
        # No bevel pass. Every surface here is round and smooth-shaded already,
        # and `bevel` at this scale welds adjacent rope turns into a solid lump
        # — the same failure `leash_device` documents for swept cable.
        p.finish(coll_name.replace("Coll_", "Mesh_"), coll)

    save(out)
    report()


main()
